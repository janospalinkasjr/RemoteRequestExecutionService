namespace RemoteExec.Api.Core.Models
{
    public class ErrorInfo
    {
        public string Code { get; set; } = "UnhandledError";
        public string Message { get; set; } = "An unexpected error occurred while processing the request.";
    }
}