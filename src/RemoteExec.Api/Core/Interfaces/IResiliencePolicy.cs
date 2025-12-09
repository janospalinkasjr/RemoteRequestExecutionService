namespace RemoteExec.Api.Core.Interfaces
{
    public interface IResiliencePolicy
    {
        Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action, 
            CancellationToken cancellationToken);
    }
}