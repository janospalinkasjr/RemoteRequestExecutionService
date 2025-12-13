using Microsoft.Extensions.Options;
using RemoteExec.Api.Configuration;
using RemoteExec.Api.Core.Exceptions;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;
using ExecutionContext = RemoteExec.Api.Core.Models.ExecutionContext;

namespace RemoteExec.Api.Infrastructure.Resilience;

public class ResiliencePolicy : IExecutionPolicy
{
    private readonly IOptions<ResilienceConfig> _config;
    private readonly ILogger<ResiliencePolicy> _logger;

    private readonly object _lock = new();
    private CircuitState _state = CircuitState.Closed;
    private int _consecutiveFailures = 0;
    private DateTime _lastFailureTimeUtc = DateTime.MinValue;

    public ResiliencePolicy(IOptions<ResilienceConfig> config, ILogger<ResiliencePolicy> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken, ExecutionDelegate next)
    {
        var cfg = _config.Value;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    var elapsed = DateTime.UtcNow - _lastFailureTimeUtc;
                    if (elapsed.TotalMilliseconds >= cfg.CircuitBreakerDurationMs)
                    {
                        _state = CircuitState.HalfOpen;
                    }
                    else
                    {
                        throw new CircuitBreakerOpenException("Circuit is OPEN. Requests are temporarily blocked.");
                    }
                }
            }

            try
            {
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(cfg.TimeoutPerAttemptMs));

                var result = await next(context, timeoutCts.Token);

                lock (_lock)
                {
                    _state = CircuitState.Closed;
                    _consecutiveFailures = 0;
                }

                return result;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < cfg.MaxRetries)
            {
                attempt++;

                lock (_lock)
                {
                    _consecutiveFailures++;
                    _lastFailureTimeUtc = DateTime.UtcNow;

                    if (_consecutiveFailures >= cfg.CircuitBreakerFailureThreshold)
                    {
                        _state = CircuitState.Open;
                    }
                }

                var delay = ComputeDelay(attempt);
                _logger.LogWarning(ex, "Transient failure on attempt {Attempt}. Retrying after {Delay}ms.", attempt, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _consecutiveFailures++;
                    _lastFailureTimeUtc = DateTime.UtcNow;

                    if (_consecutiveFailures >= cfg.CircuitBreakerFailureThreshold)
                    {
                        _state = CircuitState.Open;
                    }
                }

                _logger.LogError(ex, "Non-transient failure in resilience policy.");
                throw;
            }
        }
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        var cfg = _config.Value;

        var baseDelay = Math.Min(
            cfg.BaseDelayMs * Math.Pow(2, attempt),
            cfg.MaxDelayMs);

        double jitter = 0;
        if (cfg.JitterFactor > 0)
        {
            jitter = Random.Shared.NextDouble() * cfg.JitterFactor * baseDelay;
        }

        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    private bool IsTransient(Exception ex)
    {
        if (ex is OperationCanceledException)
            return false;

        if (ex is CircuitBreakerOpenException)
            return false;

        return true;
    }

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}
