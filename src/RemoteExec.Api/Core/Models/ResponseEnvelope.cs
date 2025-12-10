namespace RemoteExec.Api.Core.Models
{
    public class ResponseEnvelope
    {
        public string RequestId { get; set; } = "";
        public string? CorrelationId { get; set; }
        public string Status { get; set; } = "Unknown";
        public object? Result { get; set; }
        public ResilienceSummary? Resilience { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}