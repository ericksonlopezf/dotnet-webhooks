# Frequently Asked Questions (FAQ) — EricksonLopez.Webhooks

### 1. Why is comparing signatures using `string.Equals()` or `==` insecure?
Conventional string comparisons terminate immediately upon encountering the first mismatched character. Attackers on public networks can measure microscopic variations in HTTP response latencies (timing side-channel leaks) to deduce valid HMAC signature hashes byte-by-byte. `EricksonLopez.Webhooks` exclusively employs `CryptographicOperations.FixedTimeEquals` over UTF-8 byte spans, ensuring constant-time evaluation across the entire digest regardless of matches.

---

### 2. How does the library defend against Replay Attacks?
The library implements a multi-layered defense strategy:
1. **Anti-Replay Timestamp Tolerance**: Inbound requests include a Unix timestamp header. The receiver evaluates the timestamp against `TimeProvider.GetUtcNow()`. Any request with a timestamp drifting beyond `TimestampTolerance` (default: 5 minutes) is immediately rejected.
2. **Pluggable Replay Detection (`IWebhookReplayDetector`)**: For strict idempotency within the tolerance window, `InMemoryWebhookReplayDetector` (single-node) or `RedisWebhookReplayDetector` (clustered multi-node via atomic `SETNX`) records message identifiers (`webhook-id` or `X-Webhook-Delivery-Id`), rejecting duplicate requests.

---

### 3. How does zero-downtime secret rotation work?
By configuring multiple concurrent keys in `WebhookReceiverOptions` via `AddSecret("new_key")`. During the key transition window, `WebhookValidator` evaluates incoming signatures against all active keys in constant time within a single pass over the payload stream. The request is accepted if it matches any active secret.

---

### 4. Is the library 100% compatible with Native AOT and Trimming?
Yes. The entire codebase adheres strictly to zero-reflection invariants:
- No dynamic reflection calls (`Type.GetType`, `MakeGenericType`, `Activator.CreateInstance`).
- Compiles with `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` and `<IsAotCompatible>true</IsAotCompatible>`.
- Internal error responses serialize using source-generated compile-time `JsonSerializerContext`.
- For typed payload dispatching, `WebhookSenderExtensions.SendAsync<T>` accepts source-generated `JsonTypeInfo<T>` metadata.

---

### 5. Why is `InMemoryWebhookDeadLetterSink` restricted to testing and development environments?
Retaining failed payloads in heap memory introduces two severe operational vulnerabilities in production:
1. **Memory Exhaustion (OOM)**: A sustained network outage against an external receiver causes thousands of failed events to accumulate, exhausting process memory.
2. **Audit Loss**: If the container restarts or crashes, all failed events stored in memory are lost forever.
`InMemoryWebhookDeadLetterSink` includes a bounded capacity and `DropOldest` eviction policy for testing, but production environments must use `RedisWebhookDeadLetterSink` or a durable message queue.

---

### 6. How does the engine handle large payloads without exhausting memory?
- **Streaming Overloads**: Payloads can be signed and verified incrementally using `WebhookSigner.ComputeSignatureAsync` and `WebhookSigner.VerifySignatureAsync` directly over `Stream` buffers rented from `ArrayPool<byte>.Shared`, avoiding Large Object Heap (LOH) fragmentation.
- **Maximum Payload Bounds**: Both sender and receiver configure `MaxPayloadSizeBytes` (default: 10 MB). Excessively large requests are rejected immediately with HTTP 413 Payload Too Large.

---

### 7. How does SSRF protection prevent DNS Rebinding attacks?
Standard `HttpClient` preflight checks that resolve DNS before dispatch are vulnerable to Time-of-Check to Time-of-Use (TOCTOU) DNS rebinding attacks. `SafeSocketsHttpHandlerFactory` (via `EricksonLopez.Security.Network`) validates the target IP address at the exact millisecond of TCP socket connection, ensuring that private IPs (RFC 1918), loopback, link-local, and cloud metadata addresses (`169.254.169.254`) are blocked at the transport layer.

---

### 8. Does the library support both enterprise headers and StandardWebhooks?
Yes. `WebhookProtocol` supports both:
- **EricksonLopez Enterprise Protocol**: `X-Webhook-Signature` (`v1=<hex>`), `X-Webhook-Timestamp`, `X-Webhook-Delivery-Id`, `X-Webhook-Event`.
- **StandardWebhooks Specification**: `webhook-signature` (`v1,<base64>`), `webhook-timestamp`, `webhook-id`.
Receivers configured with `WebhookReceiverProtocol.AutoDetect` automatically detect the appropriate protocol from the incoming headers.
