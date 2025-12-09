using System.Text.Json;
using RemoteExec.Api.Core.Models;

namespace RemoteRequestExecutionService.Core.Interfaces
{
    public interface IExecutor
    {
        string Name { get; }
        Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken);
    }
}
