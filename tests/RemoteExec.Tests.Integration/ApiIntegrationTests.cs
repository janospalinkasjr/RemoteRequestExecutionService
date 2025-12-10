using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RemoteExec.Tests.Integration
{
    public class ApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public ApiIntegrationTests(TestWebApplicationFactory factory)
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
            var client = _factory.CreateClient();

            var requestBody = new
            {
                url = "https://example.com",
                method = "GET"
            };

            var response = await client.PostAsJsonAsync("/api/http/test", requestBody);

            var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope>();
            Assert.NotNull(envelope);
            Assert.False(string.IsNullOrWhiteSpace(envelope!.RequestId));
            Assert.False(string.IsNullOrWhiteSpace(envelope.Status));
        }

        [Fact]
        public async Task Metrics_ReturnsSnapshot()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/metrics");

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            Assert.NotNull(content);
        }

        [Fact]
        public async Task CatchAll_PowerShell_ShouldWork()
        {
            var client = _factory.CreateClient();
            var requestBody = new
            {
                command = "Get-Date"
            };

            var response = await client.PostAsJsonAsync("/api/powershell/test_ps", requestBody);

            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope>();
            Assert.NotNull(envelope);
            Assert.Equal("Success", envelope!.Status);
        }

        [Fact]
        public async Task CatchAll_PropagatesCorrelationId()
        {
            var client = _factory.CreateClient();
            var correlationId = Guid.NewGuid().ToString();

            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            var requestBody = new { command = "Get-Date" };

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

    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IHttpClientFactory))
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddHttpClient("UniversalClient")
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler());
            });
        }
    }

    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("stubbed-response")
            };

            return Task.FromResult(response);
        }
    }
}