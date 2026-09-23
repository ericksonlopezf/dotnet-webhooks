# ADR-002: Package Existence & Invariant Justification for EricksonLopez.Webhooks

## Status
Accepted (Amended — Invariant 3 superseded by ADR-011)

## Date
2026-09-04

## Context
Under EricksonLopez Design Principles 14 ("Every abstraction must justify its existence") and 15 ("Complexity must be paid for deliberately"), libraries in the ecosystem cannot exist merely as convenience wrappers around third-party or BCL features. They must defend critical architectural invariants that cannot be satisfied by standard primitives.

A formal evaluation was conducted to determine whether webhooks should remain an autonomous Tier 0 package (`EricksonLopez.Webhooks`) or be replaced by ad-hoc `HttpClient` calls, internal message brokers (`EricksonLopez.Messaging`), or generic resilience libraries (`Polly`).

## Decision
We formally approve `EricksonLopez.Webhooks` as an autonomous Tier 0 foundational package within the EricksonLopez ecosystem.

Its existence is justified by the following non-negotiable invariants:
1. **Timing Side-Channel Immunity**: `WebhookSigner.VerifySignature` mandates `CryptographicOperations.FixedTimeEquals` for HMAC-SHA256 signature verification, preventing byte-by-byte secret extraction.
2. **Anti-Replay Attack Guarantee**: Inbound requests must prove freshness via `X-Webhook-Timestamp` within a strict drift tolerance window evaluated against `TimeProvider`.
3. **Resilient Outbound Dispatch**: Outbound delivery delegates retries, rate-limiting, and backoff to platform-native Polly handlers via `AddStandardResilienceHandler()` (amended by [ADR-011](./adr-011-standard-resilience-handler-delegation.md); sender operates as a single-attempt pure dispatcher).
4. **Guaranteed Failure Traceability**: Exhausted dispatches are routed to `IWebhookDeadLetterSink` with complete diagnostic envelopes rather than silently failing.
5. **Non-Destructive Inbound Stream Buffering**: Inbound ASP.NET Core middleware enables stream buffering without breaking downstream model binding and controller pipelines.
6. **Protocol Isolation**: Isolates untrusted external HTTP integrations from internal reliable message buses.
7. **Structured Logging & Diagnostics**: `WebhookSender` emits structured, contextual diagnostic logs (`ILogger<WebhookSender>`) tracking `DeliveryId`, `EventType`, `TargetUrl`, `StatusCode`, attempt counts, backoff delay, and DLQ escalation, defaulting safely to `NullLogger<WebhookSender>.Instance` in zero-dependency and Native AOT environments.
8. **Zero-Downtime Secret Rotation**: Inbound verification evaluates against multiple active secret keys (`IReadOnlyList<string> SecretKeys`), allowing seamless cryptographic key rotation across external providers (e.g., periodic rotation policies) without service downtime or receipt failure.

### Clarification on Dead-Letter Queue Durability
The core library provides `InMemoryWebhookDeadLetterSink` strictly as a lightweight, thread-safe buffer for development, testing, and single-instance deployments. For mission-critical durability across process restarts:
- Outbound delivery should be orchestrated via `EricksonLopez.Outbox`, ensuring transactional staging in the application database before dispatch.
- Persistent DLQ sinks should implement `IWebhookDeadLetterSink` against durable storage (e.g., PostgreSQL, cloud queues, or dedicated persistence extensions). Core Tier 0 abstractions do not impose ORM or relational schema dependencies.

## Consequences

### Positive
- Enforces uniform cryptographic standards across all external integrations.
- Prevents socket and thread pool starvation in outbound workers.
- Guarantees zero unhandled exceptions via functional `Result<T>` integration.
- Provides production-grade observability via structured log events without reflection.
- Eliminates receiver downtime during cryptographic secret rotation.

### Negative
- Requires maintaining the ASP.NET Core integration adapter (`EricksonLopez.Webhooks.AspNetCore`) alongside core abstractions.

## References
- [Architectural Justification Document](../architectural-justification.md)
- [ADR-001: Webhook Delivery Model](./adr-001-webhook-delivery-model.md)
- [ADR-003: Subscription Management is Application Responsibility](./adr-003-subscription-management-scope-boundary.md)
- [ADR-004: OpenTelemetry Observability](./adr-004-opentelemetry-observability.md)
- [ADR-005: Dual-Protocol Header Compatibility](./adr-005-dual-protocol-header-compatibility.md)
- [ADR-006: Performance Benchmarking Harness and Allocation Budgets](./adr-006-performance-benchmarking-and-allocation-budgets.md)
- [ADR-011: Outbound Resilience Delegation via Microsoft.Extensions.Http.Resilience](./adr-011-standard-resilience-handler-delegation.md)
