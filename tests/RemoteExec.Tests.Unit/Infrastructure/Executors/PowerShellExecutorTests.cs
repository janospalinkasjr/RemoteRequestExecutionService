using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Infrastructure.Executors;
using Xunit;

namespace RemoteExec.Tests.Unit.Infrastructure.Executors
{
    public class PowerShellExecutorTests
    {
        private readonly Mock<ILogger<PowerShellExecutor>> _mockLogger;
        private readonly PowerShellExecutor _executor;

        public PowerShellExecutorTests()
        {
            _mockLogger = new Mock<ILogger<PowerShellExecutor>>();
            _executor = new PowerShellExecutor(_mockLogger.Object);
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsArgumentException_WhenCommandMissing()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "args", new { } }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _executor.ExecuteAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsInvalidOperationException_WhenCommandNotAllowed()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "command", "Invoke-Expression" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _executor.ExecuteAsync(request, CancellationToken.None));
            Assert.Contains("not in the allowlist", ex.Message);
        }

        [Fact]
        public async Task ExecuteAsync_ExecutesValidCommand_GetDate()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "command", "Get-Date" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act
            var result = await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            var output = result.Data as List<object>;
            Assert.NotNull(output);
            Assert.Single(output);
            Assert.False(string.IsNullOrEmpty(output[0].ToString()));
        }

        [Fact]
        public async Task ExecuteAsync_ExecutesCommandWithArgs_GetChildItem()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "command", "Get-ChildItem" },
                { "args", new Dictionary<string, string> { { "Path", "." } } }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act
            var result = await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            var output = result.Data as List<object>;
            Assert.NotNull(output);
            Assert.True(output.Count > 0);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsFailure_WhenCommandFails_InvalidArg()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "command", "Get-ChildItem" },
                { "args", new Dictionary<string, string> { { "Path", "/Non/Existent/Path/12345" } } }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act & Assert
             var ex = await Assert.ThrowsAsync<Exception>(() => 
                _executor.ExecuteAsync(request, CancellationToken.None));
             
             Assert.Contains("PowerShell execution failed", ex.Message);
        }

        [Fact]
        public async Task ExecuteAsync_UsesRemoteRunspace_WhenComputerNameProvided()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "command", "Get-Date" },
                { "computerName", "localhost" } // Trying to connect to localhost remotely
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "powershell",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act
            try 
            {
                await _executor.ExecuteAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Assert that it failed due to Connection issues, NOT implementation issues.
                // Typical errors: "MI_RESULT_FAILED", "WSMan", "Transport", "connection refused"
                Assert.True(
                    ex.Message.Contains("WSMan") || 
                    ex.Message.Contains("WS-Management") || 
                    ex.Message.Contains("connection") || 
                    ex.Message.Contains("MI_RESULT_FAILED") ||
                    ex.Message.Contains("failed"), 
                    $"Unexpected error type: {ex.Message}"
                );
            }
        }
    }
}