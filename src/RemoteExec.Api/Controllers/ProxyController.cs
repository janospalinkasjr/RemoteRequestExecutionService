using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RemoteExec.Api.Core.Models;
using RemoteExec.Api.Core.Interfaces;

namespace RemoteExec.Api.Controllers
{
    [ApiController]
    [Route("api")]
    public class ProxyController : ControllerBase
    {
        private readonly IRequestOrchestrator _orchestrator;
        private readonly ILogger<ProxyController> _logger;

        public ProxyController(IRequestOrchestrator orchestrator, ILogger<ProxyController> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        [HttpGet("ping")]
        public IActionResult Ping() => Ok("pong");

        [HttpGet("metrics")]
        public IActionResult Metrics([FromServices] Core.Interfaces.IMetricsCollector metrics) 
            => Ok(metrics.GetSnapshot());

        [Route("{**catchAll}")]
        [HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch]
        public async Task<IActionResult> HandleRequest(string catchAll)
        {
            string executorType = "http";
            string targetPath = catchAll;

            var segments = catchAll.Split('/');
            if (segments.Length > 0 && (segments[0] == "http" || segments[0] == "powershell"))
            {
                executorType = segments[0];
                targetPath = string.Join("/", segments.Skip(1));
            }

            string bodyRef = "";
            using (var reader = new StreamReader(Request.Body))
            {
                bodyRef = await reader.ReadToEndAsync();
            }

            JsonElement payload;
            try 
            {
                 if (string.IsNullOrWhiteSpace(bodyRef)) 
                 {
                     payload = JsonSerializer.SerializeToElement(new { 
                         url = "https://example.com/" + targetPath,
                         method = Request.Method
                     });
                 }
                 else 
                 {
                     payload = JsonSerializer.Deserialize<JsonElement>(bodyRef);
                 }
            }
            catch
            {
                // Fallback to raw body
                payload = JsonSerializer.SerializeToElement(new { rawBody = bodyRef });
            }

             var execReq = new ExecutionRequest
             {
                 ExecutorType = executorType,
                 Payload = payload,
                 PathInfo = targetPath,
                 CorrelationId = Request.Headers["X-Correlation-ID"].FirstOrDefault()
             };

            var response = await _orchestrator.HandleRequestAsync(execReq, HttpContext.RequestAborted);
            
            return response.Status == "Success" ? Ok(response) : StatusCode(500, response);
        }
    }
}