using RemoteExec.Api.Core.Models;

namespace RemoteExec.Api.Core.Interfaces
{
    public interface IRequestOrchestrator
    {
        Task<ResponseEnvelope> HandleRequestAsync(ExecutionRequest request, CancellationToken ct);
    }
}