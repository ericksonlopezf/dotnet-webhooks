# ADR-011: Outbound Resilience Delegation via Microsoft.Extensions.Http.Resilience

## Status
Accepted

## Date
2026-09-13

## Context
In early architectural iterations (ratified under Invariant 3 of ADR-002), `WebhookSender` was designed to execute internal retry loops with bounded exponential backoff and jitter calculated directly via `RandomNumberGenerator.GetInt32`. 

While this kept `WebhookSender` self-contained for standalone instantiation, it introduced significant architectural drawbacks in enterprise environments:
1. **Coupling to Transport Mechanics**: Embedding retry loops, backoff delays, and thread sleep/delay cycles inside the domain dispatcher violated Single Responsibility and prevented integration with standard .NET platform resilience pipelines.
2. **Duplication of Resilience Standards**: .NET 8+ and modern cloud architectures standardize on `Microsoft.Extensions.Http.Resilience` (backed by Polly v8), providing battle-tested rate-limiting, circuit breakers, hedging, and telemetry.
3. **Double Retries and Resource Saturation**: When combined with an `HttpClient` configured in application DI pipelines that also had retry policies attached, internal retries in `WebhookSender` caused multiplicative retry storms against downstream endpoints.
4. **Cancellation and Resource Leaks**: Managing multiple asynchronous delays and socket lifetimes internally complicated cancellation token propagation and clean resource disposal.

## Decision
We formally amend Invariant 3 of ADR-002 and adopt platform-native resilience delegation:

1. **Pure Single-Attempt Dispatcher**:
   - `WebhookSender.SendAsync` executes exactly **one** HTTP request per invocation (`Attempts = 1`).
   - It captures the immediate response or transport error, validates payload limits, executes SSRF preflight, computes cryptographic signatures, and records execution metrics into `WebhookDeliveryResult`.
   - If the delivery fails terminally, it routes the envelope to `IWebhookDeadLetterSink` (if configured) and returns a typed `Result<WebhookDeliveryResult>.Failure(WebhookErrors.DeliveryFailed(...))`.

2. **Delegation to Polly via `AddStandardResilienceHandler`**:
   - In standard dependency injection environments, `services.AddWebhooks()` registers the underlying `HttpClient` using `AddStandardResilienceHandler()`.
   - The resilience pipeline manages:
     - Rate-limit handling (`HTTP 429` backoffs and upstream delay windows).
     - Bounded exponential backoff with jitter on transient failures (`HTTP 5xx`, `HTTP 408`, connection drops).
     - Circuit breaker mechanics to prevent saturating failing external partners.
     - Overall and per-attempt timeout enforcement.

3. **Standalone Factory Integrity**:
   - For non-DI consumers using `WebhookSender.CreateSafeSender()`, `WebhookSender` leverages `SafeSocketsHttpHandlerFactory` to guarantee SSRF protection at the socket layer. Standalone consumers retain full control over external retry wrappers.

## Consequences

### Positive
- **Architectural Purity**: `WebhookSender` focuses strictly on webhook domain responsibilities: cryptographic HMAC generation, header adherence, SSRF validation, and telemetry.
- **Enterprise Ecosystem Alignment**: 100% interoperable with Microsoft's official cloud resilience guidance and OpenTelemetry enrichment emitted by Polly v8.
- **Zero Retry Storms**: Eliminates accidental multiplicative retry loops across nested layers.
- **Native AOT & Trimming Compatibility**: `Microsoft.Extensions.Http.Resilience` and source-generated handlers preserve zero-reflection invariants.

### Negative
- Standalone consumers invoking `new WebhookSender(httpClient)` must configure retry pipelines on their `HttpClient` or orchestrate retries at the application / Outbox level.

## References
- [ADR-001: Webhook Delivery Model](adr-001-webhook-delivery-model.md)
- [ADR-002: Package Existence & Invariant Justification](adr-002-package-existence-justification.md)
- [Best Practices Guide](../best-practices.md)
