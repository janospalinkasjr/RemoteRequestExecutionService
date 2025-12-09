using Microsoft.Extensions.Options;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Configuration;

namespace RemoteExec.Api.Infrastructure.Resilience
{
    public class CustomResiliencePolicy : IResiliencePolicy
    {
        private readonly ResilienceConfig _config;
        private readonly ILogger<CustomResiliencePolicy> _logger;
        private readonly Random _random = new Random();

        public CustomResiliencePolicy(IOptions<ResilienceConfig> config, ILogger<CustomResiliencePolicy> logger)
        {
            _config = config.Value;
            _logger = logger;
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            int attempt = 0;
            List<Exception> exceptions = new();

            while (true)
            {
                attempt++;
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(_config.TimeoutPerAttemptMs);

                try
                {
                    return await action(attemptCts.Token);
                }
                catch (OperationCanceledException ex) when (attemptCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Per-attempt timeout
                    exceptions.Add(new TimeoutException($"Attempt {attempt} timed out after {_config.TimeoutPerAttemptMs}ms", ex));
                }
                catch (Exception ex)
                {
                    // Check if transient? For now assume all exceptions are failures we might retry if configured.
                    // In a real scenario we'd check for specific status codes or exception types.
                    exceptions.Add(ex);
                    
                    // IF fatal (non-transient), break immediately.
                    // For this challenge, let's assume ArgumentException is fatal.
                    if (ex is ArgumentException) throw; 
                }

                if (attempt > _config.MaxRetries)
                {
                    throw new AggregateException("Max retries exceeded", exceptions);
                }

                // Backoff
                var delay = CalculateDelay(attempt);
                _logger.LogWarning("Attempt {Attempt} failed. Retrying in {Delay}ms.", attempt, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }

        private int CalculateDelay(int attempt)
        {
            // Exponential Backoff: Base * 2^(attempt-1)
            double expoBackoff = _config.BaseDelayMs * Math.Pow(2, attempt - 1);
            
            // Cap
            if (expoBackoff > _config.MaxDelayMs) expoBackoff = _config.MaxDelayMs;

            // Jitter: +/- JitterFactor
            double jitter = expoBackoff * _config.JitterFactor * (_random.NextDouble() * 2 - 1); // Range [-Factor, +Factor]
            
            return (int)Math.Max(0, expoBackoff + jitter);
        }
    }
}