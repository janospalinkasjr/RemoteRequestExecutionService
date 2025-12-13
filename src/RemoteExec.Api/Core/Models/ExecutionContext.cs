namespace RemoteExec.Api.Core.Models
{
    /// <summary>
    /// Holds metadata for the current execution pipeline.
    /// This is NOT a domain model and should not contain control flow primitives.
    /// </summary>
    public class ExecutionContext
    {
        public string RequestId { get; }
        public string CorrelationId { get; }

        public ExecutionContext(string requestId, string correlationId)
        {
            RequestId = requestId;
            CorrelationId = correlationId;
        }
    }
}
