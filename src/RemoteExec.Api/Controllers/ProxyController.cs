using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Core.Models;

namespace RemoteExec.Api.Controllers
{
    [ApiController]
    [Route("api")]
    public class ProxyController : ControllerBase
    {
        private readonly IRequestOrchestrator _orchestrator;
        private readonly IMetricsCollector _metrics;
        private readonly ILogger<ProxyController> _logger;

        public ProxyController(
            IRequestOrchestrator orchestrator,
            IMetricsCollector metrics,
            ILogger<ProxyController> logger)
        {
            _orchestrator = orchestrator;
            _metrics = metrics;
            _logger = logger;
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("pong");
        }

        [HttpGet("metrics")]
        public IActionResult Metrics()
        {
            var snapshot = _metrics.GetSnapshot();
            return Ok(snapshot);
        }

        [HttpGet("{executorType}/{**targetPath}")]
        [HttpPost("{executorType}/{**targetPath}")]
        [HttpPut("{executorType}/{**targetPath}")]
        [HttpDelete("{executorType}/{**targetPath}")]
        [HttpPatch("{executorType}/{**targetPath}")]
        public async Task<IActionResult> Handle(string executorType, string? targetPath = null)
        {
            JsonElement payload;

            if (Request.ContentLength is null or 0)
            {
                payload = JsonDocument.Parse("{}").RootElement;
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(body))
                {
                    payload = JsonDocument.Parse("{}").RootElement;
                }
                else
                {
                    payload = JsonDocument.Parse(body).RootElement;
                }
            }

            var existingRequestId = Request.Headers["X-Request-ID"].FirstOrDefault();
            var effectiveRequestId = string.IsNullOrWhiteSpace(existingRequestId)
                ? Guid.NewGuid().ToString()
                : existingRequestId;

            var execReq = new ExecutionRequest
            {
                RequestId = effectiveRequestId,
                ExecutorType = executorType,
                Payload = payload,
                PathInfo = targetPath,
                CorrelationId = Request.Headers["X-Correlation-ID"].FirstOrDefault(),
                Context = new Dictionary<string, string>
                {
                    ["Method"] = Request.Method,
                    ["QueryString"] = Request.QueryString.Value ?? string.Empty
                }
            };

            var response = await _orchestrator.HandleRequestAsync(execReq, HttpContext.RequestAborted);

            if (!string.IsNullOrEmpty(response.RequestId))
            {
                Response.Headers["X-Request-ID"] = response.RequestId;
            }
            else
            {
                Response.Headers["X-Request-ID"] = effectiveRequestId;
            }

            if (!string.IsNullOrEmpty(response.CorrelationId))
            {
                Response.Headers["X-Correlation-ID"] = response.CorrelationId;
            }

            if (response.Resilience != null)
            {
                Response.Headers["X-Attempt-Count"] = response.Resilience.TotalAttempts.ToString();
            }

            return MapToHttpResult(response);
        }

        private IActionResult MapToHttpResult(ResponseEnvelope response)
        {
            switch (response.Status)
            {
                case "Success":
                    return Ok(response);

                case "ValidationError":
                case "BadRequest":
                case "ExecutorNotSupported":
                case "ExecutorNotFound":
                    return BadRequest(response);

                case "NotFound":
                case "TargetNotFound":
                    return NotFound(response);

                case "Throttled":
                case "RateLimited":
                    return StatusCode(StatusCodes.Status429TooManyRequests, response);

                case "CircuitOpen":
                case "TransientFailure":
                case "ServiceUnavailable":
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, response);

                default:
                    return StatusCode(StatusCodes.Status500InternalServerError, response);
            }
        }
    }
}