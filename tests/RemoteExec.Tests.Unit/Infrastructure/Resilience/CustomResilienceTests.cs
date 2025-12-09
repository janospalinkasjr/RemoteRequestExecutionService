using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RemoteExec.Api.Configuration;
using RemoteExec.Api.Infrastructure.Resilience;

namespace RemoteExec.Tests.Unit.Infrastructure.Resilience
{
    public class CustomResilienceTests
    {

        private readonly Mock<IOptions<ResilienceConfig>> _mockConfig;
        private readonly Mock<ILogger<CustomResiliencePolicy>> _mockLogger;
        private readonly ResilienceConfig _config;

        public CustomResilienceTests()
        {
            _config = new ResilienceConfig
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerDurationMs = 100, // Short duration for testing
                MaxRetries = 0,
                BaseDelayMs = 0
            };

            _mockConfig = new Mock<IOptions<ResilienceConfig>>();
            _mockConfig.Setup(c => c.Value).Returns(_config);
            _mockLogger = new Mock<ILogger<CustomResiliencePolicy>>();
        }

        [Fact]
        public async Task ExecuteAsync_OpensCircuit_AfterThresholdFailures()
        {
            // Arrange
            var policy = new CustomResiliencePolicy(_mockConfig.Object, _mockLogger.Object);
            var actionCallCount = 0;

            // Act
            // 1. First Failure
            await Assert.ThrowsAsync<AggregateException>(() => policy.ExecuteAsync(async ct => { actionCallCount++; throw new Exception("Fail 1"); return true; }, CancellationToken.None));
            
            // 2. Second Failure (Threshold Reached -> Open -> Throws Open Exception on Exit Check)
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { actionCallCount++; throw new Exception("Fail 2"); return true; }, CancellationToken.None));

            // 3. Third Call should fail FAST with CircuitBreakerOpenException
            var ex = await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { actionCallCount++; return true; }, CancellationToken.None));

            // Assert
            Assert.Equal(2, actionCallCount); // Only called twice, third rejected
            Assert.Contains("Circuit is OPEN", ex.Message);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToClosed_OnSuccess()
        {
            // Arrange
            var policy = new CustomResiliencePolicy(_mockConfig.Object, _mockLogger.Object);

            // 1. Force Open
            await Assert.ThrowsAsync<AggregateException>(() => policy.ExecuteAsync(async ct => { throw new Exception("Fail 1"); return true; }, CancellationToken.None));
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { throw new Exception("Fail 2"); return true; }, CancellationToken.None));

            // 2. Wait for Duration (100ms)
            await Task.Delay(150);

            // 3. Next call should probe (Half-Open) and Succeed -> Close
            var result = await policy.ExecuteAsync(async ct => { return "Success"; }, CancellationToken.None);

            // 4. Following calls should work normally (Closed)
            var result2 = await policy.ExecuteAsync(async ct => { return "Success 2"; }, CancellationToken.None);

            // Assert
            Assert.Equal("Success", result);
            Assert.Equal("Success 2", result2);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToOpen_OnFailure()
        {
            // Arrange
            var policy = new CustomResiliencePolicy(_mockConfig.Object, _mockLogger.Object);

            // 1. Force Open
            await Assert.ThrowsAsync<AggregateException>(() => policy.ExecuteAsync(async ct => { throw new Exception("Fail 1"); return true; }, CancellationToken.None));
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { throw new Exception("Fail 2"); return true; }, CancellationToken.None));

            // 2. Wait for Duration
            await Task.Delay(150);

            // 3. Next call probes (Half-Open) -> Fails -> Re-opens
            // Note: The failure *inside* the probe might throw AggregateException first?
            // Let's trace: Probe runs -> Fails -> ReportFailure (Transitions to Open) -> attempt > maxRetries -> CheckCircuit -> Throws OpenException!
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { throw new Exception("Probe Failed"); return true; }, CancellationToken.None));

            // 4. Immediate next call should be rejected (Open)
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => policy.ExecuteAsync(async ct => { return true; }, CancellationToken.None));
        }

        [Fact]
        public async Task ExecuteAsync_ShouldRetry_OnFailure()
        {
            // Arrange
            var config = Options.Create(new ResilienceConfig 
            { 
                MaxRetries = 2, 
                BaseDelayMs = 1,
                MaxDelayMs = 10,
                JitterFactor = 0
            });
            var logger = new Mock<ILogger<CustomResiliencePolicy>>();
            var policy = new CustomResiliencePolicy(config, logger.Object);

            int attempts = 0;

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(async () => 
            {
                await policy.ExecuteAsync<bool>(ct =>
                {
                    attempts++;
                    return Task.FromException<bool>(new Exception("Simulated Failure"));
                }, CancellationToken.None);
            });

            // Assert
            Assert.Equal(3, attempts); // 1 initial + 2 retries
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnSuccess_AfterRetry()
        {
            // Arrange
            var config = Options.Create(new ResilienceConfig 
            { 
                MaxRetries = 3, 
                BaseDelayMs = 1 
            });
            var policy = new CustomResiliencePolicy(config, Mock.Of<ILogger<CustomResiliencePolicy>>());

            int attempts = 0;

            // Act
            var result = await policy.ExecuteAsync(ct =>
            {
                attempts++;

                if (attempts < 2)
                {
                    return Task.FromException<string>(new Exception("Temp Failure"));
                }

                return Task.FromResult("Success");
            }, CancellationToken.None);

            // Assert
            Assert.Equal("Success", result);
            Assert.Equal(2, attempts);
        }
    }
}