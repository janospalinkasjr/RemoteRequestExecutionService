namespace RemoteExec.Api.Core.Models
{
    public class ExecutionResult
    {
        public bool IsSuccess { get; set; }
        public int AttemptCount { get; set; }
        public object? Data { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
    }
}