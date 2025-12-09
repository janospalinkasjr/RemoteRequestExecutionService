using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace RemoteExec.Tests.Integration
{
    public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApiIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Ping_ReturnsPong()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/ping");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("pong", content);
        }

        [Fact]
        public async Task CatchAll_Http_ShouldWork()
        {
            // Arrange
            var client = _factory.CreateClient();
            
            var requestBody = new 
            {
                url = "https://example.com", // This might fail without internet
                method = "GET"
            };

            // Act & Assert
            var response = await client.PostAsJsonAsync("/api/http/test", requestBody);
            Assert.True(response.IsSuccessStatusCode);
            
            var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope>();
            Assert.NotNull(envelope);
            Assert.NotNull(envelope.RequestId);
            Assert.Contains(envelope.Status, new[] { "Success", "Failed" });
        }

        [Fact]
        public async Task Metrics_ReturnsSnapshot()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act & Assert
            var response = await client.GetAsync("/api/metrics");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            Assert.NotNull(content);
        }

        [Fact]
        public async Task CatchAll_PowerShell_ShouldWork()
        {
            // Arrange
            var client = _factory.CreateClient();
            var requestBody = new 
            {
                command = "Get-Date"
            };

            // Act & Assert
            var response = await client.PostAsJsonAsync("/api/powershell/test_ps", requestBody);
            Assert.True(response.IsSuccessStatusCode);

            var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope>();
            Assert.NotNull(envelope);
            Assert.Equal("Success", envelope.Status);
        }

        [Fact]
        public async Task CatchAll_PropagatesCorrelationId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var correlationId = Guid.NewGuid().ToString();
            
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            var requestBody = new { command = "Get-Date" };

            // Act & Assert
            var response = await client.PostAsJsonAsync("/api/powershell/correlation_test", requestBody);
            Assert.True(response.Headers.Contains("X-Correlation-ID"));
            Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").First());
            
            var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope>();
            Assert.NotNull(envelope);
        }
    }

    public class ResponseEnvelope
    {
        public string? RequestId { get; set; }
        public string? Status { get; set; }
    }
}