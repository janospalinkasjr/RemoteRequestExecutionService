using Microsoft.Extensions.Logging;
using RemoteExec.Api.Core.Exceptions;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;

namespace RemoteExec.Api.Infrastructure.Services
{
    public class RequestOrchestrator : IRequestOrchestrator
    {
        private readonly IEnumerable<IExecutor> _executors;
        private readonly IResiliencePolicy _resiliencePolicy;
        private readonly IMetricsCollector _metrics;
        private readonly ILogger<RequestOrchestrator> _logger;

        public RequestOrchestrator(
            IEnumerable<IExecutor> executors,
            IResiliencePolicy resiliencePolicy,
            IMetricsCollector metrics,
            ILogger<RequestOrchestrator> logger)
        {
            _executors = executors;
            _resiliencePolicy = resiliencePolicy;
            _metrics = metrics;
            _logger = logger;
        }

        public async Task<ResponseEnvelope> HandleRequestAsync(ExecutionRequest request, CancellationToken ct)
        {
            var envelope = new ResponseEnvelope
            {
                RequestId = request.RequestId,
                CorrelationId = request.CorrelationId,
                TimestampUtc = DateTime.UtcNow,
                Resilience = new ResilienceSummary()
            };

            var resilienceSummary = envelope.Resilience!;

            try
            {
                _metrics.RecordRequest(request.ExecutorType);

                var executor = _executors.FirstOrDefault(e =>
                    string.Equals(e.Name, request.ExecutorType, StringComparison.OrdinalIgnoreCase));

                if (executor == null)
                {
                    throw new NotSupportedException($"Executor type '{request.ExecutorType}' is not supported.");
                }

                var result = await _resiliencePolicy.ExecuteAsync(async token =>
                {
                    resilienceSummary.TotalAttempts++;

                    try
                    {
                        var res = await executor.ExecuteAsync(request, token);
                        resilienceSummary.AttemptOutcomes.Add(res.IsSuccess ? "Success" : "Failure");
                        return res;
                    }
                    catch
                    {
                        resilienceSummary.AttemptOutcomes.Add("Exception");
                        throw;
                    }
                }, ct);

                envelope.Status = result.IsSuccess ? "Success" : "Failed";
                envelope.Result = result.Data;

                _metrics.RecordSuccess(request.ExecutorType);
                _metrics.RecordLatency(
                    request.ExecutorType,
                    (result.EndTimeUtc - result.StartTimeUtc).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                envelope.Status = "Failed";

                var error = new ErrorInfo();

                if (ex is NotSupportedException)
                {
                    error.Code = "ExecutorNotSupported";
                    error.Message = ex.Message;
                }
                else if (ex is CircuitBreakerOpenException)
                {
                    error.Code = "CircuitOpen";
                    error.Message = "Circuit is open. Requests are temporarily blocked.";
                }
                else
                {
                    error.Code = "UnhandledError";
                    error.Message = "An unexpected error occurred while processing the request.";
                }

                envelope.Result = error;

                _metrics.RecordFailure(request.ExecutorType, false);
                _logger.LogError(ex, "Error handling request for executor {ExecutorType}", request.ExecutorType);
            }

            return envelope;
        }
    }
}