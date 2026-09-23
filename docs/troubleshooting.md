# Troubleshooting & Common Pitfalls — EricksonLopez.Webhooks

A diagnostic guide for identifying, resolving, and preventing common errors when dispatching or receiving webhooks with `EricksonLopez.Webhooks`.

---

## 1. Inbound Ingress Error: HTTP 401 Unauthorized

When an incoming webhook is rejected with HTTP 401 Unauthorized, the response JSON body includes a machine-readable `code` and descriptive `error` message.

### Common Error Codes & Mitigations:

| Error Code | Root Cause | Recommended Remediation |
|---|---|---|
| **`WebhookValidator.MissingSignature`** | The request did not transmit the signature header (`X-Webhook-Signature` or `webhook-signature`). | Verify that the sending platform has configured the signature header. If behind Cloudflare or an API Gateway, verify the proxy preserves custom headers. |
| **`WebhookValidator.InvalidTimestamp`** | The timestamp header (`X-Webhook-Timestamp` or `webhook-timestamp`) is missing or cannot be parsed as a 64-bit integer Unix timestamp. | Ensure timestamps are transmitted as Unix epoch seconds (e.g. `1757760000`). |
| **`WebhookValidator.TimestampExpired`** | The timestamp drifts beyond the configured `TimestampTolerance` window (default: 5 minutes) relative to the receiver's `TimeProvider`. | Check NTP time synchronization on both the sending host and receiver cluster. In multi-region deployments with high latency, consider widening `TimestampTolerance` (e.g. 10 minutes). |
| **`WebhookValidator.InvalidSignature`** | 1. Secret key mismatch.<br/>2. Reverse proxy or WAF mutated the request body in transit (e.g. altering JSON formatting, indentation, or newline characters `\r\n` vs `\n`).<br/>3. Wire protocol mismatch (hex vs base64). | 1. Confirm the shared symmetric secret matches.<br/>2. Ensure reverse proxies treat webhook endpoints as raw binary passthroughs without re-serializing JSON.<br/>3. Verify whether the sender formats as `X-Webhook-*` or StandardWebhooks `webhook-*`. |
| **`WebhookValidator.ReplayDetected`** | The unique message identifier (`webhook-id` or `X-Webhook-Delivery-Id`) was already successfully recorded within the retention window. | Indicates an upstream retry of an already-processed event. Verify whether the sender resends payloads on slow acknowledgements. |

---

## 2. Inbound Ingress Error: HTTP 413 Payload Too Large

### Root Cause:
The incoming HTTP request body exceeds `MaxPayloadSizeBytes` (default: 10 MB).

### Remediation:
- If high-volume batch payloads are expected, increase the limit in options:
  ```csharp
  builder.Services.AddWebhookReceiver(options =>
  {
      options.MaxPayloadSizeBytes = 25 * 1024 * 1024; // 25 MB
  });
  ```
- If payloads are unexpectedly large, verify whether an adversary is attempting an algorithmic denial-of-service attack.

---

## 3. Outbound Dispatch Error: `WebhookErrors.SecurityViolation`

### Root Cause:
`SafeSocketsHttpHandlerFactory` blocked the connection due to an SSRF violation:
1. Target URL resolved to an IPv4/IPv6 loopback address (`127.0.0.1`, `::1`).
2. Target URL resolved to an internal private network address (RFC 1918 subnets `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
3. Target URL resolved to a cloud metadata service endpoint (`http://169.254.169.254`).
4. Target URL specifies an unencrypted `http://` scheme while `DangerousAllowInsecureHttp = false`.

### Remediation:
- In production, **never disable SSRF defense**. The destination URL must resolve to a valid, publicly routable IP address.
- In local development harnesses needing to call `localhost`:
  ```csharp
  options.AllowPrivateNetworks = true;
  options.DangerousAllowInsecureHttp = true;
  ```

---

## 4. Inbound Stream Drained or Model Binding Failure in Controllers

### Symptom:
Downstream controllers or Minimal API route delegates report an empty request body or throw `JsonException` ("The input does not contain any JSON tokens").

### Root Cause:
A custom middleware or filter read `HttpContext.Request.Body` before `WebhookReceiverMiddleware` or without enabling buffering.

### Remediation:
Always apply `.RequireWebhookSignature()` or ensure `app.UseWebhookReceiver()` is registered before custom route delegates. The validator automatically invokes `context.Request.EnableBuffering()` and rewinds `context.Request.Body.Position = 0` upon completing verification.

---

## 5. Redis DLQ or Replay Detector Connection Timeouts

### Symptom:
Inbound validations or dead-letter escalations fail with `RedisConnectionException` or `TimeoutException`.

### Remediation:
1. Ensure the Redis connection string specifies `abortConnect=false` to permit background reconnection during transient network blips:
   ```csharp
   services.AddSingleton<IConnectionMultiplexer>(sp => 
       ConnectionMultiplexer.Connect("redis.internal:6379,abortConnect=false,connectTimeout=5000"));
   ```
2. Verify Redis network firewall rules allow TCP traffic on port 6379 from the application subnet.

---

## 6. Non-POST Requests Unexpectedly Validated at the Webhook Path

### Symptom:
PATCH, PUT, or DELETE requests sent to the webhook `RoutePath` are being rejected with HTTP 401 `InvalidSignature`, even though they are not webhook deliveries.

### Root Cause:
`ValidatePostOnly = false` (the default) causes `WebhookReceiverMiddleware` to validate **all** HTTP methods at the matching `RoutePath`, except OPTIONS, GET, and HEAD which are always passed through.

### Remediation:
If your webhook endpoint exclusively accepts POST requests, set `ValidatePostOnly = true` in `WebhookReceiverOptions`:

```csharp
builder.Services.AddWebhookReceiver(options =>
{
    options.RoutePath = "/api/webhooks/incoming";
    options.SecretKey = "whsec_...";
    options.ValidatePostOnly = true; // Only POST requests are intercepted
});
```

Alternatively, map non-webhook HTTP methods to different paths to avoid overlap with the webhook `RoutePath`.

> **Method bypass rules (not configurable):** OPTIONS, GET, and HEAD requests are **always** passed through to downstream handlers, regardless of `ValidatePostOnly` or `RoutePath`.
