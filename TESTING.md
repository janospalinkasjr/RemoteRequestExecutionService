# Testing Strategy

## Overview

The testing strategy focuses on strict unit testing for the complex logic (Resilience, Validation) and lightweight integration testing for the API surface.

## Test Matrix

| Scenario            | Executor   | Expected Outcome    | Transient? | Retries Used   | Assertions                           |
| ------------------- | ---------- | ------------------- | ---------- | -------------- | ------------------------------------ |
| Valid HTTP GET      | HTTP       | Success (200)       | No         | 0              | Result contains data, Status=Success |
| Invalid URL         | HTTP       | Failed              | No         | 0              | Error message present                |
| Network Timeout     | HTTP       | Failed (eventually) | Yes        | Configured Max | AttemptCount = Max + 1               |
| Allowlisted Command | PowerShell | Success             | No         | 0              | Output contains expected string      |
| Arbitrary Command   | PowerShell | Failed (Validation) | No         | 0              | Error "Not allowlisted"              |
| API Ping            | N/A        | Success ("pont")    | No         | 0              | Body == "pong"                       |

## Unit Test Suites

The following test suites provide high code coverage for core components:

### [HttpExecutorTests]

- **Scope**: `HttpExecutor` class.
- **Coverage**:
  - Validates correct `HttpRequestMessage` construction (Method, URL, Body, Headers).
  - Verifies response truncation logic (>1000 chars).
  - Checks error handling for non-success status codes.

### [PowerShellExecutorTests]

- **Scope**: `PowerShellExecutor` class.
- **Coverage**:
  - **Security**: Ensures strictly allowlisted commands (`Get-Date`, `Get-ChildItem`) can run.
  - **Validation**: Verifies blocked commands throw `InvalidOperationException`.
  - **Execution**: Confirms output collection and error stream handling.

### [InMemoryMetricsCollectorTests]

- **Scope**: `InMemoryMetricsCollector` class.
- **Coverage**: 100% of public API.
  - Verifies thread-safe increment of `Total`, `Success`, `Failed` counters.
  - Validates `Average` and `P95` latency calculations.

### [RequestOrchestratorTests]

- **Scope**: `RequestOrchestrator` class.
- **Coverage**:
  - **Integration**: Mocks all dependencies (`IExecutor`, `IResiliencePolicy`, etc.) to verify interaction.
  - **Resilience**: Simulates policy callbacks to verify retry tracking (`TotalAttempts`, `AttemptOutcomes`).
  - **Routing**: Verifies correct Executor selection based on request type.

### [CircuitBreakerTests]

- **Scope**: `CustomResiliencePolicy` (Circuit Breaker Logic).
- **Coverage**:
  - **State Transitions**: Closed -> Survivor -> Open -> HalfOpen -> Closed.
  - **Thresholds**: Verifies failure counts trip the breaker.
  - **Timers**: Verifies duration expiration transitions to Half-Open.

### [LogSanitizerTests]

- **Scope**: `LogSanitizer` class.
- **Coverage**:
  - **Redaction**: Verifies `Authorization` headers and `password` JSON fields are replaced with `***REDACTED***`.
  - **Safety**: Ensures non-sensitive logs are preserved.

### [PowerShellRemoteTests]

- **Scope**: `PowerShellExecutor` (Remote Logic).
- **Coverage**:
  - **Connection**: Verifies `WSManConnectionInfo` is initialized when `computerName` is present.
  - **Fallback**: Verifies correct handling (or expected failure) when remote target is unreachable, ensuring logic path is exercised.

## Coverage Rationale

- **ResiliencePolicyTests**: Verifies the core math and retry limits, which is the most error-prone logical component.
- **ApiIntegrationTests**: Verifies the wiring of the ASP.NET Core pipeline, Dependency Injection, JSON Serialization, and full request flow (Happy Path, Error Path, Correlation ID).
- **Component Unit Tests**: Dedicated tests for Executors and Orchestrator ensure strict input validation and correct internal logic independent of the web stack.
