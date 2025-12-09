namespace RemoteExec.Api.Core.Models
{
    public class ResilienceSummary
    {
        public int TotalAttempts { get; set; }
        public bool WasThrottled { get; set; }
        public List<string> AttemptOutcomes { get; set; } = new();
    }
}