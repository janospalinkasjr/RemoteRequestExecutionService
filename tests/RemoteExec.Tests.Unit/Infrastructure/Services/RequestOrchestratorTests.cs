using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using RemoteExec.Api.Core.Exceptions;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Infrastructure.Services;

namespace RemoteExec.Tests.Unit.Infrastructure.Services
{
    public class RequestOrchestratorTests
    {
        private readonly Mock<IExecutor> _httpExecutorMock;
        private readonly Mock<IResiliencePolicy> _policyMock;
        private readonly Mock<IMetricsCollector> _metricsMock;
        private readonly Mock<ILogger<RequestOrchestrator>> _loggerMock;

        public RequestOrchestratorTests()
        {
            _httpExecutorMock = new Mock<IExecutor>();
            _httpExecutorMock.SetupGet(e => e.Name).Returns("http");

            _policyMock = new Mock<IResiliencePolicy>();
            _metricsMock = new Mock<IMetricsCollector>();
            _loggerMock = new Mock<ILogger<RequestOrchestrator>>();
        }

        private static ExecutionRequest CreateRequest(string executorType = "http")
        {
            var payload = JsonDocument.Parse("{}").RootElement;

            return new ExecutionRequest
            {
                ExecutorType = executorType,
                RequestId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(),
                Payload = payload
            };
        }

        [Fact]
        public async Task HandleRequestAsync_UsesMatchingExecutor()
        {
            var request = CreateRequest("http");

            var execResult = new ExecutionResult
            {
                IsSuccess = true,
                StartTimeUtc = DateTime.UtcNow,
                EndTimeUtc = DateTime.UtcNow.AddMilliseconds(10),
                Data = "ok"
            };

            _httpExecutorMock
                .Setup(e => e.ExecuteAsync(It.IsAny<ExecutionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(execResult);

            _policyMock
                .Setup(p => p.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<ExecutionResult>>, CancellationToken>((action, ct) => action(ct));

            var orchestrator = new RequestOrchestrator(
                new[] { _httpExecutorMock.Object },
                _policyMock.Object,
                _metricsMock.Object,
                _loggerMock.Object);

            var envelope = await orchestrator.HandleRequestAsync(request, CancellationToken.None);

            Assert.Equal("Success", envelope.Status);
            Assert.Equal("ok", envelope.Result);
            _httpExecutorMock.Verify(e => e.ExecuteAsync(It.IsAny<ExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleRequestAsync_ReturnsError_WhenExecutorNotFound()
        {
            var request = CreateRequest("unknown");

            var orchestrator = new RequestOrchestrator(
                Array.Empty<IExecutor>(),
                _policyMock.Object,
                _metricsMock.Object,
                _loggerMock.Object);

            var envelope = await orchestrator.HandleRequestAsync(request, CancellationToken.None);

            Assert.Equal("Failed", envelope.Status);
            var error = Assert.IsType<ErrorInfo>(envelope.Result);
            Assert.Equal("ExecutorNotSupported", error.Code);
        }

        [Fact]
        public async Task HandleRequestAsync_MapsCircuitBreakerOpen_ToCircuitError()
        {
            var request = CreateRequest("http");

            _policyMock
                .Setup(p => p.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CircuitBreakerOpenException("Circuit is OPEN"));

            var orchestrator = new RequestOrchestrator(
                new[] { _httpExecutorMock.Object },
                _policyMock.Object,
                _metricsMock.Object,
                _loggerMock.Object);

            var envelope = await orchestrator.HandleRequestAsync(request, CancellationToken.None);

            Assert.Equal("Failed", envelope.Status);
            var error = Assert.IsType<ErrorInfo>(envelope.Result);
            Assert.Equal("CircuitOpen", error.Code);
            Assert.Equal("Circuit is open. Requests are temporarily blocked.", error.Message);
        }

        [Fact]
        public async Task HandleRequestAsync_ReturnsSafeUnhandledError()
        {
            var request = CreateRequest("http");

            _policyMock
                .Setup(p => p.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Something internal"));

            var orchestrator = new RequestOrchestrator(
                new[] { _httpExecutorMock.Object },
                _policyMock.Object,
                _metricsMock.Object,
                _loggerMock.Object);

            var envelope = await orchestrator.HandleRequestAsync(request, CancellationToken.None);

            Assert.Equal("Failed", envelope.Status);
            var error = Assert.IsType<ErrorInfo>(envelope.Result);
            Assert.Equal("UnhandledError", error.Code);
            Assert.Equal("An unexpected error occurred while processing the request.", error.Message);
        }

        [Fact]
        public async Task HandleRequestAsync_CapturesResilienceRetries()
        {
            var request = CreateRequest("http");

            var execResult = new ExecutionResult
            {
                IsSuccess = true,
                StartTimeUtc = DateTime.UtcNow,
                EndTimeUtc = DateTime.UtcNow.AddMilliseconds(5),
                Data = "ok-after-retry"
            };

            var callCount = 0;

            _httpExecutorMock
                .Setup(e => e.ExecuteAsync(It.IsAny<ExecutionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        throw new Exception("Transient Error");
                    }

                    return execResult;
                });

            _policyMock
                .Setup(p => p.ExecuteAsync(It.IsAny<Func<CancellationToken, Task<ExecutionResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<ExecutionResult>>, CancellationToken>(async (action, ct) =>
                {
                    try
                    {
                        return await action(ct);
                    }
                    catch
                    {
                        return await action(ct);
                    }
                });

            var orchestrator = new RequestOrchestrator(
                new[] { _httpExecutorMock.Object },
                _policyMock.Object,
                _metricsMock.Object,
                _loggerMock.Object);

            var envelope = await orchestrator.HandleRequestAsync(request, CancellationToken.None);

            Assert.Equal("Success", envelope.Status);
            Assert.Equal("ok-after-retry", envelope.Result);

            Assert.NotNull(envelope.Resilience);
            Assert.Equal(2, envelope.Resilience!.TotalAttempts);
            Assert.Contains("Exception", envelope.Resilience.AttemptOutcomes);
            Assert.Contains("Success", envelope.Resilience.AttemptOutcomes);
        }
    }
}