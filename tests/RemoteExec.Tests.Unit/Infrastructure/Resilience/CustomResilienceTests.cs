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
                CircuitBreakerDurationMs = 100,
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

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(() =>
                policy.ExecuteAsync(ct =>
                {
                    actionCallCount++;
                    return Task.FromException<bool>(new Exception("Fail 1"));
                }, CancellationToken.None));

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                {
                    actionCallCount++;
                    return Task.FromException<bool>(new Exception("Fail 2"));
                }, CancellationToken.None));

            var ex = await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                {
                    actionCallCount++;
                    return Task.FromResult(true);
                }, CancellationToken.None));

            Assert.Equal(2, actionCallCount);
            Assert.Contains("Circuit is OPEN", ex.Message);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToClosed_OnSuccess()
        {
            // Arrange
            var policy = new CustomResiliencePolicy(_mockConfig.Object, _mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromException<bool>(new Exception("Fail 1")),
                CancellationToken.None));

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromException<bool>(new Exception("Fail 2")),
                CancellationToken.None));

            await Task.Delay(150); // Half-open transition time

            var result = await policy.ExecuteAsync(ct =>
                Task.FromResult("Success"),
                CancellationToken.None);

            var result2 = await policy.ExecuteAsync(ct =>
                Task.FromResult("Success 2"),
                CancellationToken.None);

            Assert.Equal("Success", result);
            Assert.Equal("Success 2", result2);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToOpen_OnFailure()
        {
            // Arrange
            var policy = new CustomResiliencePolicy(_mockConfig.Object, _mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromException<bool>(new Exception("Fail 1")),
                CancellationToken.None));

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromException<bool>(new Exception("Fail 2")),
                CancellationToken.None));

            await Task.Delay(150);

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromException<bool>(new Exception("Probe Failed")),
                CancellationToken.None));

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ct =>
                    Task.FromResult(true),
                CancellationToken.None));
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
            await Assert.ThrowsAsync<AggregateException>(() =>
                policy.ExecuteAsync<bool>(ct =>
                {
                    attempts++;
                    return Task.FromException<bool>(new Exception("Simulated Failure"));
                }, CancellationToken.None));

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

            // Act & Assert
            var result = await policy.ExecuteAsync(ct =>
            {
                attempts++;
                if (attempts < 2)
                    return Task.FromException<string>(new Exception("Temp Failure"));

                return Task.FromResult("Success");
            }, CancellationToken.None);

            Assert.Equal("Success", result);
            Assert.Equal(2, attempts);
        }
    }
}