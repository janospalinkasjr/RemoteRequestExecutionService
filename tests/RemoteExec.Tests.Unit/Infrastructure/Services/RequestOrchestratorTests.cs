using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Infrastructure.Services;
using Moq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace RemoteExec.Tests.Unit.Infrastructure.Services
{
    public class RequestOrchestratorTests
    {
        private readonly Mock<IResiliencePolicy> _mockResiliencePolicy;
        private readonly Mock<IMetricsCollector> _mockMetrics;
        private readonly Mock<ILogger<RequestOrchestrator>> _mockLogger;
        private readonly Mock<IExecutor> _mockHttpExecutor;
        private readonly RequestOrchestrator _orchestrator;
        private readonly List<IExecutor> _executors;

        public RequestOrchestratorTests()
        {
            _mockResiliencePolicy = new Mock<IResiliencePolicy>();
            _mockMetrics = new Mock<IMetricsCollector>();
            _mockLogger = new Mock<ILogger<RequestOrchestrator>>();
            
            _mockHttpExecutor = new Mock<IExecutor>();
            _mockHttpExecutor.Setup(e => e.Name).Returns("http");

            _executors = new List<IExecutor> { _mockHttpExecutor.Object };

            _orchestrator = new RequestOrchestrator(
                _executors,
                _mockResiliencePolicy.Object,
                _mockMetrics.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task HandleRequestAsync_ReturnsFailure_WhenExecutorNotSupported()
        {
            // Arrange
            var request = new ExecutionRequest 
            { 
                ExecutorType = "unknown",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement
            };

            // Act
            var response = await _orchestrator.HandleRequestAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal("Failed", response.Status);
            Assert.NotNull(response.Result); 
            _mockMetrics.Verify(m => m.RecordRequest("unknown"), Times.Once);
            _mockMetrics.Verify(m => m.RecordFailure("unknown", false), Times.Once);
        }

        [Fact]
        public async Task HandleRequestAsync_ExecutesSuccessfully_RecordsMetrics()
        {
            // Arrange
            var request = new ExecutionRequest 
            { 
                ExecutorType = "http",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement
            };
            var expectedResult = new ExecutionResult 
            { 
                IsSuccess = true, 
                Data = "OK",
                StartTimeUtc = DateTime.UtcNow,
                EndTimeUtc = DateTime.UtcNow.AddMilliseconds(100)
            };

            _mockResiliencePolicy.Setup(p => p.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), 
                It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<ExecutionResult>>, CancellationToken>(
                    (func, ct) => func(ct));

            // Setup Executor
            _mockHttpExecutor.Setup(e => e.ExecuteAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            // Act
            var response = await _orchestrator.HandleRequestAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal("Success", response.Status);
            Assert.Equal("OK", response.Result);
            Assert.Equal(1, response.Resilience?.TotalAttempts);
            Assert.Single(response.Resilience?.AttemptOutcomes ?? new List<string>());
            Assert.Equal("Success", response.Resilience?.AttemptOutcomes.First());

            _mockMetrics.Verify(m => m.RecordRequest("http"), Times.Once);
            _mockMetrics.Verify(m => m.RecordSuccess("http"), Times.Once);
            _mockMetrics.Verify(m => m.RecordLatency("http", It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task HandleRequestAsync_CapturesResilienceRetries()
        {
            // Arrange
            var request = new ExecutionRequest 
            { 
                ExecutorType = "http",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement
            };
            var successResult = new ExecutionResult { IsSuccess = true, Data = "Finally Success", StartTimeUtc = DateTime.UtcNow, EndTimeUtc = DateTime.UtcNow };
            
            _mockResiliencePolicy.Setup(p => p.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), 
                It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<ExecutionResult>>, CancellationToken>(
                    async (func, ct) => 
                    {
                        // 1st Attempt: Fails
                        try { await func(ct); } catch { } 
                        
                        // 2nd Attempt: Succeeds
                        return await func(ct);
                    });

            _mockHttpExecutor.SetupSequence(e => e.ExecuteAsync(request, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Transient Error"))
                .ReturnsAsync(successResult);

            // Act
            var response = await _orchestrator.HandleRequestAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal("Success", response.Status);
            Assert.Equal("Finally Success", response.Result);
            
            // Verify Attempts were tracked
            Assert.Equal(2, response.Resilience?.TotalAttempts);
            Assert.Equal(2, response.Resilience?.AttemptOutcomes.Count);
            Assert.Contains("Failed: Transient Error", response.Resilience?.AttemptOutcomes[0]);
            Assert.Equal("Success", response.Resilience?.AttemptOutcomes[1]);
        }

        [Fact]
        public async Task HandleRequestAsync_ReturnsFailure_WhenExecutionThrows()
        {
             // Arrange
            var request = new ExecutionRequest 
            { 
                ExecutorType = "http",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement
            };

            // Setup Resilience Policy to bubble up exception
             _mockResiliencePolicy.Setup(p => p.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), 
                It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<ExecutionResult>>, CancellationToken>(
                    (func, ct) => func(ct));

            _mockHttpExecutor.Setup(e => e.ExecuteAsync(request, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Fatal Error"));

            // Act
            var response = await _orchestrator.HandleRequestAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal("Failed", response.Status);
            _mockMetrics.Verify(m => m.RecordFailure("http", false), Times.Once);
        }
    }
}