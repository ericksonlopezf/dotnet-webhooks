# ADR-004: OpenTelemetry Observability via ActivitySource and Meter

## Status
Accepted

## Date
2026-09-04

## Context
In modern cloud-native architectures, distributed tracing and metric emission are core operational requirements for external integrations. When webhooks fail or experience latency degradation across untrusted external networks, operators need instant distributed trace correlation (e.g., Datadog, Dynatrace, Grafana Tempo, Azure Monitor) and quantitative SLA tracking without attaching debuggers or manually tailing text logs.

Under EricksonLopez Principles 14 & 15, introducing observability must not impose heavy external SDK dependencies or compromise Native AOT compilation and zero-allocation performance guarantees.

## Decision
We formally integrate built-in OpenTelemetry instrumentation directly into `EricksonLopez.Webhooks` using BCL primitives (`System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter`):

1. **Diagnostic Source Naming**: The unified source name `EricksonLopez.Webhooks` is standardized for both tracing and metrics.
2. **Distributed Tracing (`ActivitySource`)**:
   - `WebhookSender.SendAsync` creates an activity `webhook.send` of kind `ActivityKind.Client`.
   - Tags include `webhook.delivery_id`, `webhook.event_type`, `webhook.target_url`, `http.status_code`, `webhook.attempts`, and `webhook.is_success`.
3. **Quantitative Metrics (`Meter`)**:
   - `webhook.deliveries.total` (Counter): tracks total HTTP dispatch attempts tagged by `event_type` and `is_success`.
   - `webhook.dead_letter.total` (Counter): tracks terminal failures escalated to `IWebhookDeadLetterSink`.
   - `webhook.delivery.duration` (Histogram): measures latency distributions in milliseconds.
   - `webhook.inbound.validations.total` (Counter, included in v1.0.0, Release: 2026-09-23): tracks total inbound request validation attempts in `WebhookValidator`, tagged by `reason` (e.g., `MissingSignature`, `TimestampExpired`, `InvalidSignature`, `ReplayDetected`, `PayloadTooLarge`, `Success`).
4. **Zero-Allocation When Disabled**: When no OpenTelemetry listener or diagnostic sampler is attached, `StartActivity` returns `null` immediately and meter methods do not allocate memory, maintaining zero overhead.

## Consequences

### Positive
- Enterprise-grade visibility across Datadog, Prometheus, Grafana, and OpenTelemetry collectors without bespoke wrappers.
- Zero external package dependencies beyond the standard .NET runtime.
- 100% Native AOT and trimming compliant.

### Negative
- Maintainers must ensure span names and metric dimensions remain backwards compatible across minor versions.

## References
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [Product Strategy](../product-strategy.md) (Initiative 5)

