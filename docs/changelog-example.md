# Changelog — EricksonLopez.Webhooks

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-23

### Added
- **Native AOT First-Class Support**: All core components, models, and options annotated and validated for trimming and ahead-of-time compilation.
- **`WebhookSenderExtensions.SendAsync<T>`**: Extension method enabling reflection-free Native AOT serialization with `JsonTypeInfo<T>`.
- **`EricksonLopez.Webhooks.Redis` Package**: Distributed replay detector (`RedisWebhookReplayDetector`) and dead-letter queue sink (`RedisWebhookDeadLetterSink`) backed by StackExchange.Redis.
- **`DeadLetterJsonContext`**: Source-generated `JsonSerializerContext` for `DeadLetterEnvelope` serialization.
- **Dual-Protocol Specification Support**: Full compliance with the StandardWebhooks v1 specification (`webhook-signature`, `webhook-timestamp`, `webhook-id`) alongside the native `X-Webhook-*` headers.
- **Zero-Downtime Secret Rotation**: Support for multiple active verification keys via `WebhookReceiverOptions.SecretKeys` and `AddSecret()`.
- **Minimal APIs Integration**: Added `RequireWebhookSignature()` endpoint filter extensions for `RouteHandlerBuilder` and `RouteGroupBuilder`.
- **Centralized OpenTelemetry Metrics & Tracing**: Native `ActivitySource` and `Meter` instruments in `WebhookDiagnostics`.

### Changed
- Refactored `WebhookSigner` to use `IncrementalHash` and `ArrayPool<byte>.Shared` across stream overloads, completely eliminating LOH pressure.
- `WebhookSender.SendAsync` now strictly enforces an explicit non-empty `EventId` on `WebhookMessage` to prevent duplicate deliveries.

### Security Enhancements
- Dedicated `DangerousAllowInsecureHttp` property requiring explicit security risk acknowledgment.
- `InMemoryWebhookDeadLetterSink` designated strictly for local testing with persistent `RedisWebhookDeadLetterSink` for production.

### Fixed
- Fixed timing side-channel vulnerability in signature verification by enforcing `CryptographicOperations.FixedTimeEquals` across all overloads.
- Mitigated TOCTOU DNS rebinding SSRF attacks by introducing socket-level IP validation via `SafeSocketsHttpHandlerFactory`.
