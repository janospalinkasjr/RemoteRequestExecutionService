using RemoteExec.Api.Core.Models;

namespace RemoteExec.Api.Core.Interfaces
{
    public interface IExecutor
    {
        string Name { get; }
        Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken);
    }
}
