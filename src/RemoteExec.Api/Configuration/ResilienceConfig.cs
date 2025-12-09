namespace RemoteExec.Api.Configuration
{
    public class ResilienceConfig
    {
        public int MaxRetries { get; set; } = 3;
        public int BaseDelayMs { get; set; } = 500;
        public int MaxDelayMs { get; set; } = 5000;
        public int TimeoutPerAttemptMs { get; set; } = 10000;
        public double JitterFactor { get; set; } = 0.2;
        public int CircuitBreakerFailureThreshold { get; set; } = 5;
        public int CircuitBreakerDurationMs { get; set; } = 30000;
    }
}