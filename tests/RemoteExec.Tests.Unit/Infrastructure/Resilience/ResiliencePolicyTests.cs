using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RemoteExec.Api.Configuration;
using RemoteExec.Api.Infrastructure.Resilience;
using RemoteExec.Api.Core.Exceptions;
using RemoteExec.Api.Core.Models;
using ExecutionContext = RemoteExec.Api.Core.Models.ExecutionContext;

namespace RemoteExec.Tests.Unit.Infrastructure.Resilience
{
    public class ResiliencePolicyTests
    {
        private readonly Mock<IOptions<ResilienceConfig>> _mockConfig;
        private readonly Mock<ILogger<ResiliencePolicy>> _mockLogger;
        private readonly ResilienceConfig _config;

        public ResiliencePolicyTests()
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
            _mockLogger = new Mock<ILogger<ResiliencePolicy>>();
        }

        private ExecutionContext CreateContext() => new ExecutionContext("req-id", "corr-id");

        [Fact]
        public async Task ExecuteAsync_OpensCircuit_AfterThresholdFailures()
        {
            // Arrange
            var policy = new ResiliencePolicy(_mockConfig.Object, _mockLogger.Object);
            var actionCallCount = 0;
            var ctx = CreateContext();

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) =>
                {
                    actionCallCount++;
                    return Task.FromException<ExecutionResult>(new Exception("Fail 1"));
                }));

            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) =>
                {
                    actionCallCount++;
                    return Task.FromException<ExecutionResult>(new Exception("Fail 2"));
                }));

            var ex = await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) =>
                {
                    actionCallCount++;
                    return Task.FromResult(new ExecutionResult());
                }));

            Assert.Equal(2, actionCallCount);
            Assert.Contains("Circuit is OPEN", ex.Message);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToClosed_OnSuccess()
        {
            // Arrange
            var policy = new ResiliencePolicy(_mockConfig.Object, _mockLogger.Object);
            var ctx = CreateContext();

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromException<ExecutionResult>(new Exception("Fail 1"))));

            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromException<ExecutionResult>(new Exception("Fail 2"))));

            await Task.Delay(150);

            var result = await policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromResult(new ExecutionResult { Data = "Success" }));
            var result2 = await policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromResult(new ExecutionResult { Data = "Success 2" }));

            Assert.Equal("Success", result.Data);
            Assert.Equal("Success 2", result2.Data);
        }

        [Fact]
        public async Task ExecuteAsync_HalfOpen_TransitionsToOpen_OnFailure()
        {
            // Arrange
            var policy = new ResiliencePolicy(_mockConfig.Object, _mockLogger.Object);
            var ctx = CreateContext();

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromException<ExecutionResult>(new Exception("Fail 1"))));

            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromException<ExecutionResult>(new Exception("Fail 2"))));

            await Task.Delay(150);

            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromException<ExecutionResult>(new Exception("Probe Failed"))));

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) => Task.FromResult(new ExecutionResult())));
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

            var logger = new Mock<ILogger<ResiliencePolicy>>();
            var policy = new ResiliencePolicy(config, logger.Object);
            var ctx = CreateContext();

            int attempts = 0;

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() =>
                policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) =>
                {
                    attempts++;
                    return Task.FromException<ExecutionResult>(new Exception("Simulated Failure"));
                }));

            Assert.Equal(3, attempts);
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

            var policy = new ResiliencePolicy(config, Mock.Of<ILogger<ResiliencePolicy>>());
            var ctx = CreateContext();

            int attempts = 0;

            // Act
            var result = await policy.ExecuteAsync(ctx, CancellationToken.None, (c, t) =>
            {
                attempts++;
                if (attempts < 2)
                    return Task.FromException<ExecutionResult>(new Exception("Temp Failure"));

                return Task.FromResult(new ExecutionResult { Data = "Success" });
            });

            // Assert
            Assert.Equal("Success", result.Data);
            Assert.Equal(2, attempts);
        }
    }
}
