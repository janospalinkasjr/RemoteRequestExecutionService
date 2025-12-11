# Testing Strategy

Full coverage of executors, resilience, orchestrator behavior, metrics, sanitization, and integration surfaces.

## Test Matrix

| Scenario               | Executor   | Expected   | Transient? | Retries    | Assertions          |
| ---------------------- | ---------- | ---------- | ---------- | ---------- | ------------------- |
| Valid HTTP GET         | http       | Success    | No         | 0          | Envelope ok         |
| Invalid URL            | http       | Fail       | No         | 0          | Error envelope      |
| Timeout                | http       | Fail       | Yes        | MaxRetries | Attempt summaries   |
| Allowlisted PS command | powershell | Success    | No         | 0          | Result data         |
| Disallowed PS command  | powershell | Fail       | No         | 0          | Allowlist error     |
| Circuit breaker        | any        | Fail-fast  | N/A        | 0          | CB exception        |
| Ping                   | N/A        | Success    | No         | 0          | "pong"              |
| Correlation ID         | any        | Propagated | No         | 0          | Header match        |
| Metrics                | N/A        | Success    | No         | 0          | Dictionary snapshot |

## Integration Tests

Use TestWebApplicationFactory + FakeHttpMessageHandler to ensure **no external network dependency**.

Covers:

- Ping
- HTTP executor
- PowerShell executor
- Correlation ID propagation
- Metrics endpoint
- Catch‑all forwarding

## Unit Tests

### CustomResiliencePolicyTests

- Retry flow
- Timeout enforcement
- Circuit breaker transitions

### HttpExecutorTests

- URL building
- Query/headers merge
- Truncated body

### PowerShellExecutorTests

- Allowlist enforcement
- Pipeline output handling

### RequestOrchestratorTests

- Registry-based selection
- per‑attempt metrics
- Safe error envelopes

### InMemoryMetricsCollectorTests

- Counters
- Avg and p95 latency

### LogSanitizerTests

- Token and secret masking

## Manual Validation

Ping:

```
curl http://localhost:8080/api/ping
```

HTTP:

```
curl -X POST http://localhost:8080/api/http/test  -d '{ "url": "https://example.com", "method": "GET" }'
```

PowerShell:

```
curl -X POST http://localhost:8080/api/powershell/run  -d '{ "command": "Get-Date" }'
```

Metrics:

```
curl http://localhost:8080/api/metrics
```
