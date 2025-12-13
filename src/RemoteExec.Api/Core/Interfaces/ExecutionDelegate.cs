using RemoteExec.Api.Core.Models;
using ExecutionContext = RemoteExec.Api.Core.Models.ExecutionContext;

namespace RemoteExec.Api.Core.Interfaces
{
    public delegate Task<ExecutionResult> ExecutionDelegate(ExecutionContext context, CancellationToken cancellationToken);
}
