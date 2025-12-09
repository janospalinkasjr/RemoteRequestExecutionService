using System.Text.Json;

namespace RemoteExec.Api.Core.Models
{
    public class ExecutionRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; set; }
        public required string ExecutorType { get; set; }
        public required JsonElement Payload { get; set; }
        public string? PathInfo { get; set; }
        public Dictionary<string, string> Context { get; set; } = new();
    }
}