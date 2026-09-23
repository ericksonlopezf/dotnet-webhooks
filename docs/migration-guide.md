# Migration Guide — EricksonLopez.Webhooks

This guide provides step-by-step instructions for upgrading from ad-hoc or vulnerable legacy webhook implementations to the enterprise v1.0.0 architecture (Deployment Date: 2026-09-23).

---

## 1. Migrating from Ad-Hoc Signature Verification (`==` or `string.Equals`)

### Vulnerable Legacy Pattern (Timing Attack Risk)
```csharp
// VULNERABLE ANTI-PATTERN: Leaks microsecond timing differences
if (request.Headers["X-Signature"] == computedHmac)
{
    // Process request
}
```

### Modern Secure Pattern (Constant-Time Verification)
```csharp
// SECURE PATTERN: CryptographicOperations.FixedTimeEquals over UTF-8 byte spans
var isValid = WebhookSigner.VerifySignature(secretKey, timestamp, payload, receivedSignature);

// OR declarative Minimal APIs endpoint filter:
app.MapPost("/api/webhooks", HandleWebhook)
   .RequireWebhookSignature();
```

---

## 2. Architectural Invariants & Patterns in v1.0.0 (Released: 2026-09-23)

### A. Failure Return Model in `IWebhookSender.SendAsync`
- **Design Pattern**: When outbound retries are exhausted or non-retryable 4xx client errors occur, `SendAsync` returns a deterministic functional failure with strongly typed domain errors:
  ```csharp
  // Modern v1.0.0 pattern
  var result = await sender.SendAsync(message);
  if (result.IsFailure)
  {
      // Strongly typed error code and description
      Console.WriteLine($"Error [{result.Error.Code}]: {result.Error.Description}");
  }
  ```

### B. StandardWebhooks Wire Format & Canonical String
- **Standard Implementation**: Adheres strictly to the StandardWebhooks specification format `v1,<base64>` computed over `${webhook_id}.${timestamp}.${payload}`. Senders must provide a valid `webhookId`, and receivers must parse comma-separated `v1` prefixes.

### C. Options Security Design: `DangerousAllowInsecureHttp`
To prevent accidental transmission of cleartext webhooks across the internet, cleartext HTTP requires explicit opt-in:
```csharp
options.DangerousAllowInsecureHttp = true;
```

---

## 3. Replacing `InMemoryWebhookDeadLetterSink` in Production

`InMemoryWebhookDeadLetterSink` is intended solely for local testing and development. In production, use `RedisWebhookDeadLetterSink` to prevent unbounded heap memory consumption.

### Migrating to Distributed Redis Storage:
Install `EricksonLopez.Webhooks.Redis` and configure Redis in `Program.cs`:

```csharp
// BEFORE (Testing only)
services.AddSingleton<IWebhookDeadLetterSink, InMemoryWebhookDeadLetterSink>();

// NOW (Production clustered deployment)
services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

services.AddRedisWebhookDeadLetterSink("webhooks:dead_letter_queue");
services.AddRedisWebhookReplayDetector(TimeSpan.FromMinutes(5));
```

---

## 4. Adopting Native AOT Source Generation

When publishing under .NET Native AOT (`PublishAot=true`), eliminate runtime reflection when dispatching typed payloads:

```csharp
// 1. Declare source-generated JsonSerializerContext
[JsonSerializable(typeof(InvoiceNotification))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}

// 2. Dispatch using compile-time JsonTypeInfo<T>
await sender.SendAsync(
    targetUrl,
    secretKey,
    "invoice.created",
    invoice.Id,
    invoice,
    AppJsonContext.Default.InvoiceNotification,
    cancellationToken);
```
