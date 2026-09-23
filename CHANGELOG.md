# Changelog

All notable changes to `EricksonLopez.Webhooks`, `EricksonLopez.Webhooks.AspNetCore`, and `EricksonLopez.Webhooks.Redis` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-09-23

### Added

- **Foundational Enterprise Webhook Delivery Suite**:
  - Inaugural production-grade release of `EricksonLopez.Webhooks`, `EricksonLopez.Webhooks.AspNetCore`, and `EricksonLopez.Webhooks.Redis`.
  - Multi-targeting modern .NET runtimes: `.NET 8.0`, `.NET 9.0`, and `.NET 10.0`.
  - 100% Native AOT compatible with full assembly trimming support (`IsAotCompatible = true`, `EnableTrimAnalyzer = true`) and zero runtime dynamic reflection.
  - Strong-named assemblies (`EricksonLopez.snk`) for enterprise compliance.

- **Outbound Webhook Delivery Engine (`EricksonLopez.Webhooks`)**:
  - **`IWebhookSender` & `WebhookSender`**: High-performance, resilient dispatch engine delivering authenticated webhooks with cryptographic HMAC-SHA256 signatures and anti-replay timestamps.
  - **`WebhookMessage` Primitives**: Immutable message envelope supporting both UTF-8 string and zero-allocation `ReadOnlyMemory<byte>` payloads with explicit idempotency tracking (`EventId`).
  - **`WebhookSecret` Primitives**: Immutable `readonly record struct` encapsulating raw cryptographic secrets with automatic `[REDACTED]` redaction in `ToString()` to prevent credential leakage in logging and memory dumps.
  - **Native AOT Typed Serialization**: Extension method `WebhookSenderExtensions.SendAsync<T>` integrating compile-time source-generated `JsonTypeInfo<T>` for reflection-free serialization.
  - **Deterministic Error Catalog (`WebhookErrors`)**: Railway-oriented error representation returning `Result<WebhookDeliveryResult>.Failure(WebhookErrors.DeliveryFailed(...))` with structured codes: `DeliveryFailed`, `SecurityViolation`, `Timeout`, and `InvalidConfiguration`.
  - **Terminal HTTP Status Short-Circuiting**: Categorizes HTTP 300–399 redirect responses and 4xx client errors as non-retryable failures, eliminating retry storms on permanent redirects.
  - **DoS Response Buffering Mitigation**: Dispatches with `HttpCompletionOption.ResponseHeadersRead` and truncates error snippets (`ResponseSnippet`) to prevent memory exhaustion from oversized response bodies.
  - **`WebhookPayloadTooLargeException`**: Strongly typed domain exception enforcing maximum outbound payload byte caps (`MaxPayloadSizeBytes`).

- **Zero-Trust Network Perimeter & SSRF Defense (`EricksonLopez.Security.Network`)**:
  - **Built-in SSRF Mitigation**: Enforces HTTPS by default (`RequireHttps = true`) and blocks connections to loopback (`127.0.0.0/8`, `::1`), link-local/cloud metadata (`169.254.169.254`, `fd00::/8`), and private networks (RFC 1918).
  - **DNS Rebinding Defense**: Performs preflight hostname resolution via `SafeDnsResolver` and pins socket connections through `SafeSocketsHttpHandler`.
  - **Safe Factory Instantiation**: Static factory method `WebhookSender.CreateSafeSender(...)` configuring outbound HTTP pipelines with full SSRF and DNS rebinding mitigations.
  - **Granular Security Overrides**: Optional flags `DangerousAllowInsecureHttp` and `AllowPrivateNetworks` for controlled local and internal testing environments.

- **Dual-Protocol Specification & StandardWebhooks Compliance**:
  - **StandardWebhooks Specification Support**: Full compliance with the StandardWebhooks wire format (`v1,<base64>`), space-delimited signatures, and canonical payload signing: `${webhook_id}.${timestamp}.${payload}`.
  - **Custom Enterprise Format**: Support for enterprise header conventions (`X-Webhook-*`) with signature prefix `v1=<hex>`.
  - **Dedicated Protocol Enums**: `WebhookSenderProtocol` (`EricksonLopez`, `Standard`) and `WebhookReceiverProtocol` (`AutoDetect`, `EricksonLopez`, `Standard`) ensuring compile-time separation between egress and ingress modes.
  - **Algorithmic Candidate Throttling**: Enforces a strict maximum of 5 signature candidates per header to defend against candidate explosion CPU DoS attacks.

- **Enterprise Polly Resilience Integration**:
  - Outbound retry policies, rate-limit backoff, and circuit breakers delegated to `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`).
  - Decoupled `WebhookSender` from transport retry loops, allowing fine-grained policy customization per named HTTP client.

- **ASP.NET Core Webhook Receiver (`EricksonLopez.Webhooks.AspNetCore`)**:
  - **`WebhookReceiverMiddleware`**: High-throughput ASP.NET Core middleware intercepting inbound webhooks, performing non-destructive request buffering (`EnableBuffering()`), constant-time signature evaluation, and timestamp clock-skew tolerance verification against `TimeProvider`.
  - **Native AOT Source-Generated Error Responses**: Compile-time `JsonSerializerContext` (`WebhookJsonContext`) serializing typed error responses (`WebhookErrorResponse`) without dynamic reflection.
  - **Minimal APIs Endpoint Filter (`WebhookEndpointFilter`)**: Declarative endpoint filter and route extensions `RequireWebhookSignature()` on `RouteHandlerBuilder` and `RouteGroupBuilder` for route-level signature verification.
  - **Zero-Downtime Secret Rotation**: `WebhookReceiverOptions.SecretKeys` (`ImmutableList<string>`) and fluent `AddSecret()` helper allowing multi-secret evaluation during key transition windows.
  - **Startup Configuration Validation**: Enforces eager validation at application startup via `.ValidateOnStart()` on `WebhookSenderOptions` and `WebhookReceiverOptions`.
  - **Inbound Payload Cap Defense**: Enforces `MaxPayloadSizeBytes` (default 10 MB) returning HTTP 413 Payload Too Large and code `"WebhookValidator.PayloadTooLarge"` on violation.

- **Distributed Redis Provider Package (`EricksonLopez.Webhooks.Redis`)**:
  - **`RedisWebhookDeadLetterSink`**: Production-ready distributed dead-letter queue storage utilizing atomic Redis lists.
  - **`RedisWebhookReplayDetector`**: Distributed idempotency and anti-replay verification leveraging atomic Redis keys and sliding TTL expiration.
  - **`DeadLetterJsonContext`**: Native AOT source-generated `JsonSerializerContext` for reflection-free JSON serialization of dead-letter envelopes.
  - **Fluent DI Integration**: Service collection extensions `AddRedisWebhookDeadLetterSink` and `AddRedisWebhookReplayDetector`.

- **Comprehensive Observability & Diagnostics**:
  - **Distributed Tracing (`ActivitySource`)**: Built-in BCL instrumentation for `EricksonLopez.Webhooks` (v1.0.0) with W3C TraceContext propagation (`traceparent`, `tracestate`).
  - **Quantitative Metrics (`Meter`)**: Real-time counters and histograms:
    - `webhook.deliveries.total`: Total outbound delivery attempts partitioned by `event_type` and `is_success`.
    - `webhook.dead_letter.total`: Total failed deliveries escalated to the dead-letter sink.
    - `webhook.delivery.duration`: Outbound delivery latency distributions in milliseconds.
    - `webhook.inbound.validations.total`: Total inbound validations partitioned by `is_valid` and `reason`.
  - **High-Performance Logging**: Compile-time source-generated `[LoggerMessage]` delegates with zero boxing and zero string allocations across all sender, receiver, and validator components.
