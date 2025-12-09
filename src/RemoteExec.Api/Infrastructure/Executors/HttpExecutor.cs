using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Core.Security;

namespace RemoteExec.Api.Infrastructure.Executors
{
    public class HttpExecutor : IExecutor
    {
        public string Name => "http";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HttpExecutor> _logger;

        public HttpExecutor(IHttpClientFactory httpClientFactory, ILogger<HttpExecutor> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = new ExecutionResult { StartTimeUtc = DateTime.UtcNow };
            
            try
            {
                // 1. Parse Payload
                if (!request.Payload.TryGetProperty("url", out var urlProp) || 
                    !request.Payload.TryGetProperty("method", out var methodProp))
                {
                     throw new ArgumentException("Payload must contain 'url' and 'method'");
                }

                var url = urlProp.GetString();
                var method = new HttpMethod(methodProp.GetString() ?? "GET");
                
                using var client = _httpClientFactory.CreateClient("UniversalClient");
                var httpRequest = new HttpRequestMessage(method, url);

                // Add headers if present
                if (request.Payload.TryGetProperty("headers", out var headersProp))
                {
                    foreach (var header in headersProp.EnumerateObject())
                    {
                        httpRequest.Headers.TryAddWithoutValidation(header.Name, header.Value.ToString());
                    }
                }

                // Add body if present
                if (request.Payload.TryGetProperty("body", out var bodyProp))
                {
                    var jsonBody = bodyProp.ToString();
                    httpRequest.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                    _logger.LogInformation("Added Body: {Body}", LogSanitizer.Sanitize(jsonBody));
                }

                var response = await client.SendAsync(httpRequest, cancellationToken);
                
                result.EndTimeUtc = DateTime.UtcNow;
                result.IsSuccess = response.IsSuccessStatusCode;
                result.Metadata["StatusCode"] = ((int)response.StatusCode).ToString();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                // Truncate body for safety/size
                result.Data = content.Length > 1000 ? content.Substring(0, 1000) + "...(truncated)" : content;
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
