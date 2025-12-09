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
                TimestampUtc = DateTime.UtcNow
            };
            
            var resilienceSummary = new ResilienceSummary();
            envelope.Resilience = resilienceSummary;

            try
            {
                _metrics.RecordRequest(request.ExecutorType);

                // Strategy Pattern: Select Executor
                var executor = _executors.FirstOrDefault(e => e.Name.Equals(request.ExecutorType, StringComparison.OrdinalIgnoreCase));
                if (executor == null)
                {
                    throw new NotSupportedException($"Executor type '{request.ExecutorType}' is not supported.");
                }
                
                var result = await _resiliencePolicy.ExecuteAsync(async (token) => 
                {
                    resilienceSummary.TotalAttempts++;
                    try 
                    {
                        var res = await executor.ExecuteAsync(request, token);
                        resilienceSummary.AttemptOutcomes.Add("Success");
                        return res;
                    }
                    catch (Exception ex)
                    {
                        resilienceSummary.AttemptOutcomes.Add($"Failed: {ex.Message}");
                        throw; // Re-throw for resilience to catch and decide
                    }
                }, ct);

                envelope.Status = "Success";
                envelope.Result = result.Data;
                _metrics.RecordSuccess(request.ExecutorType);
                _metrics.RecordLatency(request.ExecutorType, (result.EndTimeUtc - result.StartTimeUtc).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                envelope.Status = "Failed";
                envelope.Result = new { Error = ex.Message, Details = ex.ToString() };
                _metrics.RecordFailure(request.ExecutorType, false);
            }

            return envelope;
        }
    }
}