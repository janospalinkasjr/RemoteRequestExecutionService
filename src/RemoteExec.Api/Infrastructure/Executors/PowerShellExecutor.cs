using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;

namespace RemoteExec.Api.Infrastructure.Executors
{
    public class PowerShellExecutor : IExecutor
    {
        public string Name => "powershell";
        private readonly ILogger<PowerShellExecutor> _logger;
        private readonly HashSet<string> _allowedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "Get-Process",
            "Get-Service", 
            "Get-Date",
            "Get-ChildItem"
        };

        public PowerShellExecutor(ILogger<PowerShellExecutor> logger)
        {
            _logger = logger;
        }

        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = new ExecutionResult { StartTimeUtc = DateTime.UtcNow };

            try
            {
                if (!request.Payload.TryGetProperty("command", out var cmdProp))
                    throw new ArgumentException("Payload must contain 'command'");

                string commandName = cmdProp.GetString() ?? "";
                
                // Security Check
                if (!_allowedCommands.Contains(commandName))
                {
                    throw new InvalidOperationException($"Command '{commandName}' is not in the allowlist.");
                }

                // Create Session State (Isolation)
                var sessionState = InitialSessionState.CreateDefault();

                // Session Lifecycle: Create -> Connect (Implicit in Runspace) -> Run -> Dispose
                // Create Runspace: Remote vs Local
                Runspace runspace;
                if (request.Payload.TryGetProperty("computerName", out var computerProp) && !string.IsNullOrWhiteSpace(computerProp.GetString()))
                {
                    var computerName = computerProp.GetString();
                    var connectionInfo = new WSManConnectionInfo(new Uri($"http://{computerName}:5985/wsman"));
                    // Note: Authentication is complex. Defaulting to Default credentials.
                    // In a real scenario, we'd extract credentials from context or vault.
                    runspace = RunspaceFactory.CreateRunspace(connectionInfo);
                }
                else
                {
                    // Local Isolated Session
                    runspace = RunspaceFactory.CreateRunspace(sessionState);
                }

                try
                {
                    runspace.Open();
                    
                    using (var ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;
                        ps.AddCommand(commandName);

                        // Add parameters if any
                        if (request.Payload.TryGetProperty("args", out var argsProp))
                        {
                            foreach (var arg in argsProp.EnumerateObject())
                            {
                                ps.AddParameter(arg.Name, arg.Value.ToString());
                            }
                        }

                        // Execute
                        // Note: PowerShell.InvokeAsync is not cancellable in older versions, 
                        // but .NET Core version supports Stop() via another thread or just async wrapping.
                        // Ideally use InvokeAsync().
                        
                        var psOutput = await Task.Factory.FromAsync(
                            ps.BeginInvoke(), 
                            ps.EndInvoke
                        );

                        // Collect results
                        var outputList = new List<object>();
                        foreach (var item in psOutput)
                        {
                            outputList.Add(item.BaseObject?.ToString() ?? "null");
                        }
                        
                        result.Data = outputList;
                        result.IsSuccess = !ps.HadErrors;

                        if (ps.HadErrors)
                        {
                            foreach (var err in ps.Streams.Error)
                            {
                                result.ErrorMessages.Add(err.ToString());
                            }
                            // Treat as failure
                            throw new Exception($"PowerShell execution failed: {string.Join("; ", result.ErrorMessages)}");
                        }
                    }
                }
                finally
                {
                    runspace.Dispose();
                }
                
                result.EndTimeUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.EndTimeUtc = DateTime.UtcNow;
                result.IsSuccess = false;
                result.ErrorMessages.Add(ex.Message);
                throw;
            }

            return result;
        }
    }
}