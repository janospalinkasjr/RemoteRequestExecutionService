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
        private enum CircuitState { Closed, Open, HalfOpen }
        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private readonly object _lock = new object();

        public CustomResiliencePolicy(IOptions<ResilienceConfig> config, ILogger<CustomResiliencePolicy> logger)
        {
            _config = config.Value;
            _logger = logger;
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            CheckCircuit();

            int attempt = 0;
            List<Exception> exceptions = new();

            while (true)
            {
                attempt++;
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(_config.TimeoutPerAttemptMs);

                try
                {
                    var result = await action(attemptCts.Token);
                    
                    ReportSuccess();
                    return result;
                }
                catch (OperationCanceledException ex) when (attemptCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    exceptions.Add(new TimeoutException($"Attempt {attempt} timed out after {_config.TimeoutPerAttemptMs}ms", ex));
                    ReportFailure();
                }
                catch (CircuitBreakerOpenException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    ReportFailure();
                    
                    if (ex is ArgumentException) throw;
                }

                if (attempt > _config.MaxRetries)
                {
                    CheckCircuit();
                    throw new AggregateException("Max retries exceeded", exceptions);
                }

                CheckCircuit(); 

                var delay = CalculateDelay(attempt);
                _logger.LogWarning("Attempt {Attempt} failed. Retrying in {Delay}ms.", attempt, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }

        private void CheckCircuit()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > TimeSpan.FromMilliseconds(_config.CircuitBreakerDurationMs))
                    {
                        _state = CircuitState.HalfOpen;
                        _logger.LogInformation("Circuit Breaker transitioned to HALF-OPEN.");
                    }
                    else
                    {
                        throw new CircuitBreakerOpenException($"Circuit is OPEN. Failures: {_consecutiveFailures}");
                    }
                }
            }
        }

        private void ReportSuccess()
        {
            lock (_lock)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    _state = CircuitState.Closed;
                    _consecutiveFailures = 0;
                    _logger.LogInformation("Circuit Breaker transitioned to CLOSED (Healthy).");
                }
                else if (_state == CircuitState.Closed)
                {
                    _consecutiveFailures = 0;
                }
            }
        }

        private void ReportFailure()
        {
            lock (_lock)
            {
                _lastFailureTime = DateTime.UtcNow;

                if (_state == CircuitState.HalfOpen)
                {
                    _state = CircuitState.Open;
                    _logger.LogWarning("Circuit Breaker transitioned to OPEN (Half-Open probe failed).");
                }
                else if (_state == CircuitState.Closed)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= _config.CircuitBreakerFailureThreshold)
                    {
                        _state = CircuitState.Open;
                        _logger.LogError("Circuit Breaker transitioned to OPEN (Threshold reached).");
                    }
                }
            }
        }

        private int CalculateDelay(int attempt)
        {
            double expoBackoff = _config.BaseDelayMs * Math.Pow(2, attempt - 1);
            if (expoBackoff > _config.MaxDelayMs) expoBackoff = _config.MaxDelayMs;
            double jitter = expoBackoff * _config.JitterFactor * (_random.NextDouble() * 2 - 1);
            return (int)Math.Max(0, expoBackoff + jitter);
        }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }
}