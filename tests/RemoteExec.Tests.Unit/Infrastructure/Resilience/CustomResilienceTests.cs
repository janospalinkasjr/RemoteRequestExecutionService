using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RemoteExec.Api.Configuration;
using RemoteExec.Api.Infrastructure.Resilience;

namespace RemoteExec.Tests.Unit.Infrastructure.Resilience
{
    public class CustomResilienceTests
    {
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