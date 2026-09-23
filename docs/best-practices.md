# Best Practices & Operational Excellence — EricksonLopez.Webhooks

Architectural rules and operational guidance for engineering secure, fault-tolerant, and high-performance webhook systems using `EricksonLopez.Webhooks`.

---

## 1. Cryptographic Security & Secret Management

- **Encapsulate Secrets in `WebhookSecret`**: Always wrap raw secret strings in [`WebhookSecret`](api-reference.md#webhooksecret-struct). This struct ensures `ToString()` evaluates to `"[REDACTED]"`, preventing accidental credential leakage into APM telemetry, structured logs, or crash dumps.
- **Generate High-Entropy Symmetric Keys**: Secrets should consist of at least 32 cryptographically random bytes (256 bits) generated via `RandomNumberGenerator.GetBytes()` and encoded in Base64 or Hexadecimal format.
- **Enforce Narrow Clock Skew Tolerance**: Keep `TimestampTolerance` bounded to 5 minutes (300 seconds) or less. Wider tolerance windows expand the exposure window for replay attacks.
- **Execute Scheduled Secret Rotation**: Take advantage of zero-downtime key rotation by configuring multiple active keys in `WebhookReceiverOptions.SecretKeys`. Deploy the new key to receivers first, update senders to sign with the new key, and subsequently decommission the retired key.

---

## 2. Zero-Trust Network Defense & SSRF Mitigation

- **Mandatory SSRF Protection in Production**: Never set `EnableSsrfProtection = false` in production environments. `SafeSocketsHttpHandlerFactory` validates IP targets at connection time, neutralizing DNS rebinding attacks.
- **Disallow Private Network Access**: Ensure `AllowPrivateNetworks = false` to guarantee outbound webhooks cannot probe local loopback (`127.0.0.1`), RFC 1918 internal subnets, or cloud metadata services (`http://169.254.169.254/latest/meta-data/`).
- **Enforce Strict HTTPS Schemes**: Ensure `DangerousAllowInsecureHttp = false`. Unencrypted HTTP webhooks transmit HMAC signatures and payloads across the public internet in cleartext, enabling eavesdropping and replay attacks.
- **Preflight URI Validation**: Catch malformed target URLs early; `WebhookSender` automatically rejects non-HTTP/HTTPS schemes with `WebhookErrors.InvalidConfiguration`.

---

## 3. Memory Budgets & Large Object Heap (LOH) Defense

- **Stream Large Payloads**: For payloads exceeding 85 KB (the .NET Large Object Heap threshold), avoid converting bodies into intermediate strings. Utilize the streaming overloads `WebhookSigner.ComputeSignatureAsync` and `WebhookSigner.VerifySignatureAsync` over `Stream` buffers to maintain zero LOH fragmentation.
- **Enforce Maximum Request Body Limits**: Always configure `MaxPayloadSizeBytes` (default: 10 MB) on both senders and receivers to defend against malicious memory exhaustion and denial-of-service attempts.
- **Candidate Tokenization Ceilings**: Inbound signature verification enforces a hard limit of 5 space-delimited candidate signatures per header, blocking CPU exhaustion attacks via signature candidate explosion.

---

## 4. Distributed Resilience & At-Least-Once Delivery

- **Architect for At-Least-Once Delivery**: Network partitions and transient timeouts mean webhook deliveries may occasionally be delivered more than once. Receivers must implement idempotency checks against unique event identifiers (`eventId` / `webhook_id`).
- **Pair with the Transactional Outbox Pattern**: Rather than dispatching webhooks synchronously within business transaction requests, persist webhook events into a local database Outbox table within the same ACID transaction, and dispatch them asynchronously via a dedicated background worker (`BackgroundService`).
- **Use Distributed Dead-Letter Queues**: In multi-instance or containerized environments, avoid `InMemoryWebhookDeadLetterSink`. Utilize `RedisWebhookDeadLetterSink` (`EricksonLopez.Webhooks.Redis`) or an enterprise message broker to guarantee audit durability.
- **Enterprise Resilience via Polly**: Integrate outbound dispatchers with `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`), configuring automated backoff, circuit breaking, and rate-limiting policies on transient 429 and 5xx responses.

---

## 5. Observability & Telemetry

- **Quantitative Metric Alarms**: Configure Prometheus / Grafana alerts on `webhook.deliveries.total` (filtering on status codes) and `webhook.dead_letter.total`. A sudden surge in dead-letter counts signals downstream partner outages or invalid cryptographic secrets.
- **Distributed Trace Context Propagation**: Outbound dispatches automatically inject W3C `traceparent` and `tracestate` headers into HTTP requests, preserving distributed trace context across organizational network boundaries.
- **Zero-Allocation Structured Logging**: All logging delegates within `WebhookSender`, `WebhookValidator`, and `WebhookEndpointFilter` utilize source-generated `[LoggerMessage]` partial methods.
