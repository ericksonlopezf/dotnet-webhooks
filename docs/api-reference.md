# API Reference — EricksonLopez.Webhooks

Comprehensive, Microsoft Learn-style technical API reference for [`EricksonLopez.Webhooks`](../src/EricksonLopez.Webhooks), [`EricksonLopez.Webhooks.AspNetCore`](../src/EricksonLopez.Webhooks.AspNetCore), and [`EricksonLopez.Webhooks.Redis`](../src/EricksonLopez.Webhooks.Redis).

---

## Table of Contents

### Namespace `EricksonLopez.Webhooks` (Core Engine)
1. [`WebhookSecret`](#webhooksecret) — Memory-safe secret encapsulation.
2. [`WebhookSigner`](#webhooksigner) — Constant-time HMAC-SHA256 signature computation and verification.
3. [`IWebhookSender`](#iwebhooksender) — Outbound dispatch interface.
4. [`WebhookSender`](#webhooksender) — Outbound HTTP dispatcher with SSRF mitigation and tracing.
5. [`WebhookSenderExtensions`](#webhooksenderextensions) — Native AOT strongly-typed dispatch extensions.
6. [`WebhookSenderOptions`](#webhooksenderoptions) — Dispatcher configuration options.
7. [`WebhookSenderProtocol`](#webhooksenderprotocol) — Outbound wire protocol enum.
8. [`WebhookReceiverProtocol`](#webhookreceiverprotocol) — Inbound detection wire protocol enum.
9. [`WebhookProtocol`](#webhookprotocol) — Unified protocol enum.
10. [`WebhookHeaders`](#webhookheaders) — Canonical HTTP header name constants.
11. [`WebhookMessage`](#webhookmessage) — Immutable outbound request message model.
12. [`WebhookPayload`](#webhookpayload) — Audit envelope model for delivery attempts and DLQ persistence.
13. [`WebhookDeliveryResult`](#webhookdeliveryresult) — Value struct capturing dispatch execution outcome and metrics.
14. [`WebhookErrors`](#webhookerrors) — Strongly-typed functional domain errors (`Result<T>`).
15. [`WebhookPayloadTooLargeException`](#webhookpayloadtoolargeexception) — Exception thrown on payload ceiling violations.
16. [`IWebhookDeadLetterSink`](#iwebhookdeadlettersink) — Contract for dead-letter persistence.
17. [`NoOpWebhookDeadLetterSink`](#noopwebhookdeadlettersink) — Safe drop sink implementation.
18. [`InMemoryWebhookDeadLetterSink`](#inmemorywebhookdeadlettersink) — In-memory sink for local development and testing.
19. [`IWebhookReplayDetector`](#iwebhookreplaydetector) — Contract for message idempotency and anti-replay defense.
20. [`InMemoryWebhookReplayDetector`](#inmemorywebhookreplaydetector) — Sliding window in-memory replay detector.
21. [`WebhookDiagnostics`](#webhookdiagnostics) — OpenTelemetry `ActivitySource` and `Meter` observability instruments.
22. [`WebhookServiceCollectionExtensions`](#webhookservicecollectionextensions) — Core dependency injection extensions (`AddWebhooks`).

### Namespace `EricksonLopez.Webhooks.AspNetCore` (HTTP Ingress)
23. [`IWebhookValidator`](#iwebhookvalidator) — Inbound HTTP request cryptographic verification contract.
24. [`WebhookValidator`](#webhookvalidator) — Request validator with anti-replay and secret rotation.
25. [`WebhookReceiverOptions`](#webhookreceiveroptions) — Ingress options supporting zero-downtime rotation.
26. [`WebhookEndpointFilter`](#webhookendpointfilter) — Minimal APIs endpoint filter for per-route signature enforcement.
27. [`WebhookReceiverMiddleware`](#webhookreceivermiddleware) — Pipeline middleware for endpoint verification.
28. [`WebhookAspNetCoreExtensions`](#webhookaspnetcoreextensions) — Ingress DI, middleware, and route builder extensions.

### Namespace `EricksonLopez.Webhooks.Redis` (Distributed Infrastructure)
29. [`DeadLetterEnvelope`](#deadletterenvelope) — Serializable record wrapping DLQ items.
30. [`DeadLetterJsonContext`](#deadletterjsoncontext) — Native AOT compile-time source-generated `JsonSerializerContext`.
31. [`RedisWebhookDeadLetterSink`](#rediswebhookdeadlettersink) — Persistent distributed DLQ sink backed by Redis lists.
32. [`RedisWebhookReplayDetector`](#rediswebhookreplaydetector) — Atomic distributed anti-replay detector backed by Redis `SETNX`.
33. [`RedisWebhookServiceCollectionExtensions`](#rediswebhookservicecollectionextensions) — Redis provider DI registration extensions.

---

## `EricksonLopez.Webhooks`

### `WebhookSecret`

An immutable `readonly record struct` encapsulating raw cryptographic symmetric secrets. Overrides `ToString()` to return `"[REDACTED]"`, neutralizing accidental credential leakage in log aggregation pipelines, APM traces, and crash dumps.

#### Definition
```csharp
namespace EricksonLopez.Webhooks;

public readonly record struct WebhookSecret
```

> **Note:** `WebhookSecret` does **not** use a primary constructor. The `_secret` field is `private readonly`. There is no auto-generated public property named `secret` — accessing `.secret` will cause CS1061.

#### Constructors & Methods
- `public WebhookSecret(string secret)`: Initializes the struct. Throws `ArgumentException` if `secret` is null or whitespace.
- `public string GetUnsecuredString()`: Unwraps the underlying raw secret string for cryptographic operations.
- `public override string ToString()`: Returns `"[REDACTED]"`.
- `public static implicit operator WebhookSecret(string secret)`: Enables direct string assignment.
- `public static implicit operator string(WebhookSecret secret)`: Enables transparent unwrapping when passing to APIs expecting string.

#### Example
```csharp
WebhookSecret secret = "whsec_live_998877665544";
Console.WriteLine(secret); // Outputs: [REDACTED]
string raw = secret.GetUnsecuredString();
```

---

### `WebhookSigner`

High-performance static cryptographic utility providing HMAC-SHA256 signature computation and constant-time verification immune to timing side-channel attacks.

#### Methods

##### `ComputeSignature(string, long, string)`
```csharp
public static string ComputeSignature(string secretKey, long timestampSeconds, string payload)
```
- **Description**: Computes a hexadecimal-encoded HMAC-SHA256 signature using the `EricksonLopez` protocol format (`v1=<hex>`).
- **Exceptions**: `ArgumentNullException` if `secretKey` or `payload` is null.

##### `ComputeSignature(string, long, string, WebhookSenderProtocol, string?)`
```csharp
public static string ComputeSignature(
    string secretKey,
    long timestampSeconds,
    string payload,
    WebhookSenderProtocol protocol,
    string? webhookId = null)
```
- **Description**: Computes signature adhering to `WebhookSenderProtocol.EricksonLopez` (hex) or `WebhookSenderProtocol.Standard` (`v1,<base64>`).
- **Exceptions**: `ArgumentException` if `protocol` is `Standard` and `webhookId` is null or whitespace.

##### `ComputeSignature(string, long, ReadOnlySpan<byte>, WebhookSenderProtocol, string?)`
```csharp
public static string ComputeSignature(
    string secretKey,
    long timestampSeconds,
    ReadOnlySpan<byte> payloadBytes,
    WebhookSenderProtocol protocol,
    string? webhookId = null)
```
- **Description**: Zero-allocation signature computation over contiguous byte spans.

##### `ComputeSignatureAsync(string, long, Stream, WebhookSenderProtocol, string?, long?, CancellationToken)`
```csharp
public static async Task<string> ComputeSignatureAsync(
    string secretKey,
    long timestampSeconds,
    Stream payloadStream,
    WebhookSenderProtocol protocol,
    string? webhookId = null,
    long? maxPayloadBytes = null,
    CancellationToken cancellationToken = default)
```
- **Description**: Computes signature iteratively over a `Stream` using `IncrementalHash` to prevent allocating large buffers on the Large Object Heap (LOH). Throws `WebhookPayloadTooLargeException` if bytes read exceed `maxPayloadBytes`.

##### `VerifySignature(string, long, string, string)`
```csharp
public static bool VerifySignature(string secretKey, long timestampSeconds, string payload, string receivedSignature)
```
- **Description**: Verifies signature in constant time using `CryptographicOperations.FixedTimeEquals`.

##### `VerifySignature(string, long, string, string, WebhookReceiverProtocol, string?)`
```csharp
public static bool VerifySignature(
    string secretKey,
    long timestampSeconds,
    string payload,
    string receivedSignature,
    WebhookReceiverProtocol protocol,
    string? webhookId = null)
```

##### `VerifySignature(string, long, ReadOnlySpan<byte>, string, WebhookReceiverProtocol, string?)`
```csharp
public static bool VerifySignature(
    string secretKey,
    long timestampSeconds,
    ReadOnlySpan<byte> payloadBytes,
    string receivedSignature,
    WebhookReceiverProtocol protocol,
    string? webhookId = null)
```

##### `VerifySignatureAsync(IEnumerable<string>, long, Stream, string, WebhookReceiverProtocol, string?, long?, CancellationToken)`
```csharp
public static async Task<bool> VerifySignatureAsync(
    IEnumerable<string> secretKeys,
    long timestampSeconds,
    Stream payloadStream,
    string receivedSignature,
    WebhookReceiverProtocol protocol,
    string? webhookId,
    long? maxPayloadBytes,
    CancellationToken cancellationToken = default)
```
- **Description**: Multi-key constant-time verification over streams with payload ceiling checks.

---

### `IWebhookSender`

Primary abstraction for dispatching outbound HTTP webhooks.

```csharp
namespace EricksonLopez.Webhooks;

public interface IWebhookSender
{
    Task<Result<WebhookDeliveryResult>> SendAsync(
        WebhookMessage message,
        CancellationToken cancellationToken = default);
}
```

---

### `WebhookSender`

Thread-safe production implementation of `IWebhookSender` featuring SSRF defense, socket connection-time IP validation, OpenTelemetry metrics, and dead-letter queue escalation.

> **Instantiation:** All `WebhookSender` constructors are `internal`. Do **not** attempt `new WebhookSender(...)` — this will result in compiler error CS0122. Obtain an instance via:
> - **Dependency Injection (recommended):** Register with `builder.Services.AddWebhooks()` and inject `IWebhookSender`.
> - **Testing / standalone:** Call the static factory `WebhookSender.CreateSafeSender(...)` which provisions a hardened `SafeSocketsHttpHandler` automatically.

#### Factory Method
```csharp
public static WebhookSender CreateSafeSender(
    IWebhookDeadLetterSink? deadLetterQueue = null,
    WebhookSenderOptions? options = null,
    ILogger<WebhookSender>? logger = null)
```
- **Description**: Instantiates `WebhookSender` wired with secure `SafeSocketsHttpHandler` to neutralize DNS rebinding TOCTOU attacks.

#### Methods
- `public Task<Result<WebhookDeliveryResult>> SendAsync(WebhookMessage message, CancellationToken cancellationToken = default)`: Dispatches the webhook request. Returns `Result.Success(WebhookDeliveryResult)` on HTTP 2xx or `Result.Failure(WebhookErrors.*)` on terminal client or network failure.

---

### `WebhookSenderExtensions`

Native AOT-compliant extensions for `IWebhookSender`.

```csharp
namespace EricksonLopez.Webhooks;

public static class WebhookSenderExtensions
{
    public static Task<Result<WebhookDeliveryResult>> SendAsync<T>(
        this IWebhookSender sender,
        Uri targetUrl,
        string secretKey,
        string eventType,
        string eventId,
        T data,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default);
}
```

---

### `WebhookSenderOptions`

Configuration options governing outbound webhook delivery.

```csharp
namespace EricksonLopez.Webhooks;

public sealed class WebhookSenderOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxPayloadSizeBytes { get; set; } = 10 * 1024 * 1024;
    public bool EnableSsrfProtection { get; set; } = true;
    public bool AllowPrivateNetworks { get; set; } = false;
    public bool DangerousAllowInsecureHttp { get; set; } = false;
    public SsrfProtectionOptions SsrfProtection { get; }
    public WebhookSenderProtocol Protocol { get; set; } = WebhookSenderProtocol.EricksonLopez;
}
```

---

### `WebhookSenderProtocol`, `WebhookReceiverProtocol`, `WebhookProtocol`

- `WebhookSenderProtocol`: Enum `{ EricksonLopez = 0, Standard = 1 }`.
- `WebhookReceiverProtocol`: Enum `{ AutoDetect = 0, EricksonLopez = 1, Standard = 2 }`.
- `WebhookProtocol`: Enum `{ EricksonLopez = 0, Standard = 1, AutoDetect = 2 }`.

---

### `WebhookHeaders`

Static constants defining standard and proprietary HTTP header names.

```csharp
namespace EricksonLopez.Webhooks;

public static class WebhookHeaders
{
    public const string Signature = "X-Webhook-Signature";
    public const string Timestamp = "X-Webhook-Timestamp";
    public const string EventType = "X-Webhook-Event";
    public const string DeliveryId = "X-Webhook-Delivery-Id";
    public const string StandardSignature = "webhook-signature";
    public const string StandardTimestamp = "webhook-timestamp";
    public const string StandardDeliveryId = "webhook-id";
    public const string StandardEventType = "webhook-event";
}
```

---

### `WebhookMessage`

Immutable record encapsulating an outbound webhook request.

```csharp
namespace EricksonLopez.Webhooks;

public sealed record WebhookMessage
{
    public WebhookMessage(Uri targetUrl, WebhookSecret secretKey, string eventType, string eventId, string payloadString);
    public WebhookMessage(Uri targetUrl, WebhookSecret secretKey, string eventType, string eventId, ReadOnlyMemory<byte> payloadBytes);

    public Uri TargetUrl { get; }
    public WebhookSecret SecretKey { get; }
    public string EventType { get; }
    public string EventId { get; }
    public string? PayloadString { get; }
    public ReadOnlyMemory<byte>? PayloadBytes { get; }
}
```

---

### `WebhookPayload`

Record representing an event envelope routed to DLQ or stored for audit.

```csharp
namespace EricksonLopez.Webhooks;

public sealed record WebhookPayload(
    string DeliveryId,
    string EventType,
    string Payload,
    DateTimeOffset Timestamp,
    int AttemptNumber = 1);
```

---

### `WebhookDeliveryResult`

Readonly record struct capturing delivery metrics and HTTP execution metadata.

```csharp
namespace EricksonLopez.Webhooks;

public readonly record struct WebhookDeliveryResult(
    bool IsSuccess,
    int StatusCode,
    int Attempts,
    TimeSpan Duration,
    string? ErrorMessage = null,
    string? ResponseSnippet = null);
```

---

### `WebhookErrors`

Factory creating strongly-typed domain errors returning `EricksonLopez.Result.Error`:
- `DeliveryFailed(Uri targetUrl, int attempts, string? detail)` — Code: `"WebhookSender.DeliveryFailed"`
- `SecurityViolation(string reason)` — Code: `"WebhookSender.SecurityViolation"`
- `Timeout(Uri targetUrl, TimeSpan timeout)` — Code: `"WebhookSender.OverallTimeout"`
- `InvalidConfiguration(string message)` — Code: `"WebhookSender.InvalidConfiguration"`
- `PayloadTooLarge(long maxSizeBytes)` — Code: `"WebhookSender.PayloadTooLarge"`

#### Inbound Validation Error Codes (`WebhookValidator`):
- `"WebhookValidator.MissingSignature"`: Required cryptographic signature header is absent.
- `"WebhookValidator.TimestampExpired"`: Timestamp skew exceeds configured `TimestampTolerance`.
- `"WebhookValidator.InvalidSignature"`: HMAC signature verification failed against all active keys.
- `"WebhookValidator.ReplayDetected"`: Identifier already recorded within the anti-replay window.
- `"WebhookValidator.PayloadTooLarge"`: Body length exceeds `MaxPayloadSizeBytes`.

---

### `WebhookPayloadTooLargeException`

Thrown when payloads or streams exceed configured byte limits.
```csharp
namespace EricksonLopez.Webhooks;

public sealed class WebhookPayloadTooLargeException : InvalidOperationException
{
    public WebhookPayloadTooLargeException(long maxBytes);
    public WebhookPayloadTooLargeException(string message);
    public WebhookPayloadTooLargeException(string message, Exception innerException);
    public long MaxBytes { get; }
}
```

---

### `IWebhookDeadLetterSink`

Abstractions for persisting failed webhooks after retries are exhausted.

```csharp
namespace EricksonLopez.Webhooks;

public interface IWebhookDeadLetterSink
{
    Task EnqueueAsync(
        Uri targetUrl,
        WebhookPayload payload,
        WebhookDeliveryResult lastResult,
        CancellationToken cancellationToken = default);
}
```

---

### `NoOpWebhookDeadLetterSink`

Safe discard implementation of `IWebhookDeadLetterSink`.

---

### `InMemoryWebhookDeadLetterSink`

Thread-safe bounded in-memory dead-letter queue.
> [!NOTE]
> Intended strictly for testing and local development to prevent unconstrained memory exhaustion. In production, use `RedisWebhookDeadLetterSink`.

---

### `IWebhookReplayDetector` & `InMemoryWebhookReplayDetector`

Contract and in-memory implementation for detecting duplicate message deliveries.

```csharp
namespace EricksonLopez.Webhooks;

public interface IWebhookReplayDetector
{
    ValueTask<bool> TryRecordAsync(string messageId, CancellationToken cancellationToken = default);
}
```
- `InMemoryWebhookReplayDetector(TimeSpan retentionPeriod)`: Thread-safe in-memory sliding window detector implementing `IDisposable`.

---

### `WebhookDiagnostics`

Centralized OpenTelemetry instrumentation instruments:
- `ActivitySource`: `"EricksonLopez.Webhooks"` (version `"1.0.0"`).
- `Meter`: `"EricksonLopez.Webhooks"` (version `"1.0.0"`).
- `DeliveriesTotal`: `Counter<long>`
- `DeadLetterEscalationsTotal`: `Counter<long>`
- `DeliveryDuration`: `Histogram<double>`
- `InboundValidationsTotal`: `Counter<long>`
- `DiagnosticSourceName`: Constant `"EricksonLopez.Webhooks"`.

---

### `WebhookServiceCollectionExtensions`

Registers outbound webhook services:
```csharp
namespace EricksonLopez.Webhooks;

public static class WebhookServiceCollectionExtensions
{
    public static IServiceCollection AddWebhooks(
        this IServiceCollection services,
        Action<WebhookSenderOptions>? configure = null);
}
```

---

## `EricksonLopez.Webhooks.AspNetCore`

### `IWebhookValidator` & `WebhookValidator`

Validates inbound HTTP requests in ASP.NET Core (`HttpContext`).

```csharp
namespace EricksonLopez.Webhooks.AspNetCore;

public interface IWebhookValidator
{
    Task<Result<bool>> ValidateRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
```

---

### `WebhookReceiverOptions`

Configuration for incoming webhook validation:
- `SecretKey`: Primary secret key.
- `SecretKeys`: Immutable list of active keys for zero-downtime rotation.
- `TimestampTolerance`: Maximum allowed drift (default: 5 minutes).
- `MaxPayloadSizeBytes`: Maximum request body size (default: 10 MB).
- `RoutePath`: Ingress route path (default: `"/api/webhooks"`).
- `Protocol`: `WebhookReceiverProtocol` (`AutoDetect`, `EricksonLopez`, `Standard`).
- `ValidatePostOnly`: Rejects non-POST methods when `true` (default: `false`). When `false`, non-POST requests pass through to downstream handlers.
- `AddSecret(string secretKey)`: Fluent method to register additional active rotation keys.

---

### `WebhookEndpointFilter`

Minimal APIs `IEndpointFilter` enforcing signature verification on mapped routes:
```csharp
app.MapPost("/webhooks", () => Results.Ok())
   .RequireWebhookSignature();
```

---

### `WebhookReceiverMiddleware`

Pipeline middleware intercepting requests at `RoutePath`, enabling non-destructive stream buffering (`EnableBuffering()`), rewinding streams upon verification, and returning JSON error responses on failure.

---

### `WebhookAspNetCoreExtensions`

Extension methods for ASP.NET Core:
- `services.AddWebhookReceiver(Action<WebhookReceiverOptions> configure)`
- `services.AddInMemoryReplayDetector(TimeSpan? retentionPeriod = null)`
- `app.UseWebhookReceiver()`
- `routeHandlerBuilder.RequireWebhookSignature()`
- `routeGroupBuilder.RequireWebhookSignature()`

---

## `EricksonLopez.Webhooks.Redis`

### `DeadLetterEnvelope` & `DeadLetterJsonContext`

Data contract and source-generated Native AOT `JsonSerializerContext` for storing DLQ entries into Redis lists without reflection.

```csharp
namespace EricksonLopez.Webhooks.Redis;

public sealed record DeadLetterEnvelope(
    string TargetUrl,
    WebhookPayload Payload,
    WebhookDeliveryResult LastResult);
```

---

### `RedisWebhookDeadLetterSink`

Durable `IWebhookDeadLetterSink` implementation backed by Redis `RPUSH`.
- `public RedisWebhookDeadLetterSink(IConnectionMultiplexer connectionMultiplexer, string listKey = "webhook:dlq")`

---

### `RedisWebhookReplayDetector`

Distributed `IWebhookReplayDetector` implementation using atomic Redis `SET ... NX EX` commands to prevent replay attacks across clustered microservices.
- `public RedisWebhookReplayDetector(IConnectionMultiplexer connectionMultiplexer, TimeSpan retentionPeriod)`

---

### `RedisWebhookServiceCollectionExtensions`

Dependency injection extensions for Redis:
- `services.AddRedisWebhookReplayDetector(TimeSpan? retentionPeriod = null)`
- `services.AddRedisWebhookDeadLetterSink(string listKey = "webhook:dlq")`
