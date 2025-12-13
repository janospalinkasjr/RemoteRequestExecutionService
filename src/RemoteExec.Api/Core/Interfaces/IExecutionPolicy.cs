using RemoteExec.Api.Core.Models;
using ExecutionContext = RemoteExec.Api.Core.Models.ExecutionContext;

namespace RemoteExec.Api.Core.Interfaces
{
    public interface IExecutionPolicy
    {
        Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken, ExecutionDelegate next);
    }
}
