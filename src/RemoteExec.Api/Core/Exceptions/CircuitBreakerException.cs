namespace RemoteExec.Api.Core.Exceptions
{
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }
}