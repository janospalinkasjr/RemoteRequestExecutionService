using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Infrastructure.Executors;

namespace RemoteExec.Tests.Unit.Executors
{
    public class HttpExecutorTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<ILogger<HttpExecutor>> _mockLogger;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly HttpExecutor _executor;

        public HttpExecutorTests()
        {
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockLogger = new Mock<ILogger<HttpExecutor>>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();

            var client = new HttpClient(_mockHttpMessageHandler.Object);
            _mockHttpClientFactory.Setup(x => x.CreateClient("UniversalClient"))
                .Returns(client);

            _executor = new HttpExecutor(_mockHttpClientFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsArgumentException_WhenUrlMissing()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "method", "GET" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _executor.ExecuteAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsArgumentException_WhenMethodMissing()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _executor.ExecuteAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task ExecuteAsync_SendsRequest_WithCorrectMethodAndUrl()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com/api" },
                { "method", "POST" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            SetupMockResponse(HttpStatusCode.OK, "success");

            // Act
            var result = await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Post && 
                    req.RequestUri!.ToString() == "http://example.com/api"),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ExecuteAsync_AddsHeaders_WhenProvided()
        {
            // Arrange
            var headers = new Dictionary<string, string> { { "X-Test", "Value" } };
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com" },
                { "method", "GET" },
                { "headers", headers }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            SetupMockResponse(HttpStatusCode.OK, "success");

            // Act
            await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Headers.Contains("X-Test") && 
                    req.Headers.GetValues("X-Test").First() == "Value"),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ExecuteAsync_AddsBody_WhenProvided()
        {
            // Arrange
            var body = new { key = "value" };
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com" },
                { "method", "POST" },
                { "body", body }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            SetupMockResponse(HttpStatusCode.OK, "success");

            // Act
            await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Content != null && 
                    req.Content.ReadAsStringAsync().Result == "{\"key\":\"value\"}"),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsFailure_WhenStatusCodeIsNotSuccess()
        {
            // Arrange
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com" },
                { "method", "GET" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            SetupMockResponse(HttpStatusCode.NotFound, "Not Found");

            // Act
            var result = await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("404", result.Metadata["StatusCode"]);
        }

        [Fact]
        public async Task ExecuteAsync_TruncatesLargeResponse()
        {
             // Arrange
            var largeContent = new string('a', 1005);
            var payload = new Dictionary<string, object>
            {
                { "url", "http://example.com" },
                { "method", "GET" }
            };
            var request = new ExecutionRequest
            {
                ExecutorType = "http",
                Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload))
            };

            SetupMockResponse(HttpStatusCode.OK, largeContent);

            // Act
            var result = await _executor.ExecuteAsync(request, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.EndsWith("...(truncated)", result.Data?.ToString());
            Assert.True(result.Data?.ToString()?.Length <= 1050);
        }


        private void SetupMockResponse(HttpStatusCode statusCode, string content)
        {
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
        }
    }
}
