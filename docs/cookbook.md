# Official Cookbook — EricksonLopez.Webhooks

This technical cookbook provides production-ready solutions and patterns utilizing **exclusively** the official public APIs of [`EricksonLopez.Webhooks`](../src/EricksonLopez.Webhooks), [`EricksonLopez.Webhooks.AspNetCore`](../src/EricksonLopez.Webhooks.AspNetCore), and [`EricksonLopez.Webhooks.Redis`](../src/EricksonLopez.Webhooks.Redis).

---

## Recipe Index

1. [Recipe 1: Outbound Dispatch with SSRF Protection and DNS Rebinding Mitigation](#recipe-1-outbound-dispatch-with-ssrf-protection-and-dns-rebinding-mitigation)
2. [Recipe 2: Inbound Webhook Verification in Minimal APIs with Endpoint Filters](#recipe-2-inbound-webhook-verification-in-minimal-apis-with-endpoint-filters)
3. [Recipe 3: Zero-Downtime Secret Key Rotation](#recipe-3-zero-downtime-secret-key-rotation)
4. [Recipe 4: Dual-Protocol Compatibility (EricksonLopez vs. StandardWebhooks v1)](#recipe-4-dual-protocol-compatibility-ericksonlopez-vs-standardwebhooks-v1)
5. [Recipe 5: Native AOT Serialization without Reflection using JsonTypeInfo&lt;T&gt;](#recipe-5-native-aot-serialization-without-reflection-using-jsontypeinfot)
6. [Recipe 6: Dead-Letter Queue Escalation with Redis Storage](#recipe-6-dead-letter-queue-escalation-with-redis-storage)
7. [Recipe 7: Distributed Anti-Replay Attack Mitigation with Redis SETNX](#recipe-7-distributed-anti-replay-attack-mitigation-with-redis-setnx)
8. [Recipe 8: Streaming Large Payloads & Defending the Large Object Heap (LOH)](#recipe-8-streaming-large-payloads--defending-the-large-object-heap-loh)
9. [Recipe 9: Deterministic Clock Skew Unit Testing with TimeProvider](#recipe-9-deterministic-clock-skew-unit-testing-with-timeprovider)
10. [Recipe 10: OpenTelemetry Distributed Tracing & Quantitative Metrics Instrumentation](#recipe-10-opentelemetry-distributed-tracing--quantitative-metrics-instrumentation)

---

### Recipe 1: Outbound Dispatch with SSRF Protection and DNS Rebinding Mitigation

#### Problem
When dispatching webhooks to arbitrary URLs configured by external users or SaaS subscribers, an attacker might configure target URLs pointing to internal infrastructure or cloud instance metadata (e.g. `http://169.254.169.254` on AWS/GCP, or `http://127.0.0.1:8080`), or exploit Time-of-Check to Time-of-Use (TOCTOU) DNS Rebinding vulnerabilities.

#### Solution
Instantiate [`WebhookSender.CreateSafeSender`](../src/EricksonLopez.Webhooks/WebhookSender.cs) or register [`AddWebhooks`](../src/EricksonLopez.Webhooks/WebhookServiceCollectionExtensions.cs) with `EnableSsrfProtection = true`. The transport layer validates the IP address at the exact instant of TCP connection using a secure socket handler.

#### Complete Implementation
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using EricksonLopez.Webhooks;

public class OutboundDispatchService
{
    private readonly IWebhookSender _sender;

    public OutboundDispatchService(IWebhookSender sender)
    {
        _sender = sender;
    }

    public async Task DeliverNotificationAsync(Uri customerWebhookUrl, string secretKey, CancellationToken cancellationToken)
    {
        var secret = new WebhookSecret(secretKey);
        var message = new WebhookMessage(
            customerWebhookUrl,
            secret,
            "invoice.paid",
            $"evt_{Guid.NewGuid():N}",
            "{\"invoiceId\":\"inv_10293\",\"amount\":1250.00}");

        Result<WebhookDeliveryResult> result = await _sender.SendAsync(message, cancellationToken);

        if (result.IsSuccess)
        {
            Console.WriteLine($"Webhook delivered successfully. Status: {result.Value.StatusCode}, Duration: {result.Value.Duration.TotalMilliseconds:F1}ms");
        }
        else
        {
            Console.WriteLine($"Delivery failed: [{result.Error.Code}] {result.Error.Description}");
        }
    }
}
```

#### Explanation
1. [`WebhookMessage`](../src/EricksonLopez.Webhooks/WebhookMessage.cs) enforces an absolute URL, non-null `EventType`, and mandatory `EventId` for downstream idempotency tracking.
2. `IWebhookSender.SendAsync` performs URI preflight validation and delegates transport to `SafeSocketsHttpHandlerFactory`, which actively blocks loopback, private subnets (RFC 1918), and link-local ranges at socket connection time.
3. If blocked, the sender returns `WebhookErrors.SecurityViolation` without leaking unhandled socket exceptions.

#### Best Practices
- Always enforce `AllowPrivateNetworks = false` in production.
- Keep `DangerousAllowInsecureHttp = false` to mandate TLS encryption.

---

### Recipe 2: Inbound Webhook Verification in Minimal APIs with Endpoint Filters

#### Problem
You want to secure specific incoming HTTP webhook routes in ASP.NET Core Minimal APIs without intercepting every route globally in the pipeline and without destructively draining the request body stream (`HttpRequest.Body`).

#### Solution
Register [`AddWebhookReceiver`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookAspNetCoreExtensions.cs) and apply the [`RequireWebhookSignature()`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookAspNetCoreExtensions.cs) extension method to any `RouteHandlerBuilder` or `RouteGroupBuilder`.

#### Complete Implementation
```csharp
using EricksonLopez.Webhooks.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebhookReceiver(options =>
{
    options.SecretKey = builder.Configuration["Webhooks:ReceiverSecret"] ?? "whsec_default_secret_for_demo";
    options.TimestampTolerance = TimeSpan.FromMinutes(5);
    options.MaxPayloadSizeBytes = 2 * 1024 * 1024; // 2 MB limit
});

var app = builder.Build();

// Protect an entire route group
var webhookGroup = app.MapGroup("/api/v1/inbound")
    .RequireWebhookSignature();

webhookGroup.MapPost("/payments", async (HttpContext context) =>
{
    // The request body stream is buffered and rewound to position 0
    using var reader = new StreamReader(context.Request.Body);
    var payload = await reader.ReadToEndAsync();
    return Results.Ok(new { status = "received", length = payload.Length });
});

// Protect an individual route
app.MapPost("/api/v1/telemetry-hook", () => Results.NoContent())
    .RequireWebhookSignature();

app.Run();
```

#### Explanation
[`RequireWebhookSignature()`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookAspNetCoreExtensions.cs) injects [`WebhookEndpointFilter`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookEndpointFilter.cs). The filter resolves [`IWebhookValidator`](../src/EricksonLopez.Webhooks.AspNetCore/IWebhookValidator.cs), enables request stream buffering (`request.EnableBuffering()`), evaluates HMAC signatures in constant time, and resets `Body.Position = 0` for subsequent model binding.

---

### Recipe 3: Zero-Downtime Secret Key Rotation

#### Problem
Periodic or emergency secret key rotation can result in 100% rejection of in-flight webhooks if the sender and receiver are not updated simultaneously in the exact same millisecond.

#### Solution
Configure multiple concurrent active keys in [`WebhookReceiverOptions`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookReceiverOptions.cs) using `AddSecret`.

#### Complete Implementation
```csharp
using System;
using EricksonLopez.Webhooks.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

public static class SecretRotationSetup
{
    public static void ConfigureReceiverWithSecretRotation(IServiceCollection services)
    {
        services.AddWebhookReceiver(options =>
        {
            // Primary newly deployed secret that the sender will begin using
            options.SecretKey = "whsec_2026_quarter4_active_secret";

            // Previous secrets remaining valid during the transition grace period
            options.AddSecret("whsec_2026_quarter3_deprecated_secret");
            options.AddSecret("whsec_2026_quarter2_legacy_secret");

            options.TimestampTolerance = TimeSpan.FromMinutes(5);
        });
    }
}
```

#### Explanation
[`WebhookValidator`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookValidator.cs) iterates through `GetActiveSecretKeys()`. In a single read pass over the payload stream, it evaluates HMAC candidates in constant time (`FixedTimeEquals`). The request is accepted if any active key matches.

---

### Recipe 4: Dual-Protocol Compatibility (EricksonLopez vs. StandardWebhooks v1)

#### Problem
An enterprise receives webhooks from external SaaS providers following the `StandardWebhooks` specification (Svix, Stripe, GitHub) using `webhook-id`, `webhook-timestamp`, and `webhook-signature` (`v1,base64`), while also receiving internal events with enterprise headers (`X-Webhook-*`, `v1=hex`).

#### Solution
Set `Protocol = WebhookReceiverProtocol.AutoDetect` in [`WebhookReceiverOptions`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookReceiverOptions.cs).

#### Complete Implementation
```csharp
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.AspNetCore;

var options = new WebhookReceiverOptions
{
    SecretKey = "whsec_shared_dual_protocol_key",
    Protocol = WebhookReceiverProtocol.AutoDetect // Automatically detects protocol via header presence
};
```

---

### Recipe 5: Native AOT Serialization without Reflection using JsonTypeInfo&lt;T&gt;

#### Problem
In .NET Native AOT (Ahead-Of-Time) publishing, invoking `JsonSerializer.Serialize(obj)` via dynamic reflection raises IL2026/IL3050 compiler trimming warnings and can fail at runtime.

#### Solution
Use [`WebhookSenderExtensions.SendAsync<T>`](../src/EricksonLopez.Webhooks/WebhookSenderExtensions.cs) passing a compile-time source-generated `JsonTypeInfo<T>`.

#### Complete Implementation
```csharp
using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Webhooks;

public sealed record CustomerCreatedNotification(string CustomerId, string Tier, DateTime CreatedAt);

[JsonSerializable(typeof(CustomerCreatedNotification))]
internal sealed partial class NotificationJsonContext : JsonSerializerContext
{
}

public class AotWebhookPublisher
{
    private readonly IWebhookSender _sender;

    public AotWebhookPublisher(IWebhookSender sender)
    {
        _sender = sender;
    }

    public async Task PublishAsync(Uri endpoint, string secretKey, CustomerCreatedNotification data, CancellationToken ct)
    {
        await _sender.SendAsync(
            endpoint,
            secretKey,
            "customer.created",
            $"evt_cust_{data.CustomerId}",
            data,
            NotificationJsonContext.Default.CustomerCreatedNotification,
            ct);
    }
}
```

---

### Recipe 6: Dead-Letter Queue Escalation with Redis Storage

#### Problem
When a webhook delivery exhausts all configured retries or the remote endpoint returns a terminal error, the message must be durably stored across container restarts for auditing and manual replay.

#### Solution
Register [`AddRedisWebhookDeadLetterSink`](../src/EricksonLopez.Webhooks.Redis/RedisWebhookServiceCollectionExtensions.cs) from [`EricksonLopez.Webhooks.Redis`](../src/EricksonLopez.Webhooks.Redis).

#### Complete Implementation
```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.Redis;

var services = new ServiceCollection();

services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect("redis-cluster.internal:6379,abortConnect=false"));

services.AddWebhooks(options =>
{
    options.Timeout = TimeSpan.FromSeconds(10);
});

// Register durable Redis Dead-Letter Sink
services.AddRedisWebhookDeadLetterSink("webhooks:dead_letter_queue");
```

---

### Recipe 7: Distributed Anti-Replay Attack Mitigation with Redis SETNX

#### Problem
In a load-balanced cluster of ASP.NET Core ingress replicas, an attacker can replay an identical webhook against different pods. An in-memory replay detector cannot detect replayed requests routed to sibling nodes.

#### Solution
Register [`AddRedisWebhookReplayDetector`](../src/EricksonLopez.Webhooks.Redis/RedisWebhookServiceCollectionExtensions.cs), which executes atomic `SETNX` operations with TTL expiration in Redis.

#### Complete Implementation
```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using EricksonLopez.Webhooks.AspNetCore;
using EricksonLopez.Webhooks.Redis;

var services = new ServiceCollection();

services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect("redis-cluster.internal:6379"));

// Retention period should equal or slightly exceed timestamp tolerance window
services.AddRedisWebhookReplayDetector(TimeSpan.FromMinutes(10));
```

---

### Recipe 8: Streaming Large Payloads & Defending the Large Object Heap (LOH)

#### Problem
Buffering large webhook payloads (several megabytes) into contiguous UTF-16 strings causes Large Object Heap (LOH) fragmentation and high Garbage Collector latency spikes.

#### Solution
Use [`WebhookSigner.ComputeSignatureAsync`](../src/EricksonLopez.Webhooks/WebhookSigner.cs) and [`WebhookSigner.VerifySignatureAsync`](../src/EricksonLopez.Webhooks/WebhookSigner.cs) operating incrementally over `Stream` buffers rented from `ArrayPool<byte>.Shared`.

#### Complete Implementation
```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Webhooks;

public class LargeWebhookProcessor
{
    public async Task<bool> VerifyLargePayloadStreamAsync(
        Stream requestBodyStream,
        string secretKey,
        long timestamp,
        string signature,
        CancellationToken ct)
    {
        try
        {
            return await WebhookSigner.VerifySignatureAsync(
                secretKey,
                timestamp,
                requestBodyStream,
                signature,
                WebhookReceiverProtocol.AutoDetect,
                webhookId: null,
                maxPayloadBytes: 5 * 1024 * 1024, // Strict 5 MB ceiling
                cancellationToken: ct);
        }
        catch (WebhookPayloadTooLargeException ex)
        {
            Console.WriteLine($"Payload rejected for exceeding {ex.MaxBytes} bytes");
            return false;
        }
    }
}
```

---

### Recipe 9: Deterministic Clock Skew Unit Testing with TimeProvider

#### Problem
Testing that expired webhooks are correctly rejected due to clock drift usually relies on fragile `Thread.Sleep` calls or non-deterministic system clock queries.

#### Solution
Inject a `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing` when constructing [`WebhookValidator`](../src/EricksonLopez.Webhooks.AspNetCore/WebhookValidator.cs).

#### Complete Implementation
```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public class WebhookValidatorTests
{
    [Fact]
    public async Task ValidateRequestAsync_RejectsExpiredTimestamp()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = "whsec_test_secret",
            TimestampTolerance = TimeSpan.FromMinutes(5)
        });

        var validator = new WebhookValidator(options, timeProvider: fakeTime);

        var context = new DefaultHttpContext();
        // Timestamp emitted 15 minutes in the past (exceeds the 5-minute tolerance window)
        var oldTimestamp = fakeTime.GetUtcNow().AddMinutes(-15).ToUnixTimeSeconds();
        const string payload = "{\"test\":true}";
        var signature = WebhookSigner.ComputeSignature("whsec_test_secret", oldTimestamp, payload);

        context.Request.Headers[WebhookHeaders.Signature] = signature;
        context.Request.Headers[WebhookHeaders.Timestamp] = oldTimestamp.ToString();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var result = await validator.ValidateRequestAsync(context);

        Assert.True(result.IsFailure);
        Assert.Equal("WebhookValidator.TimestampExpired", result.Error.Code);
    }
}
```

---

### Recipe 10: OpenTelemetry Distributed Tracing & Quantitative Metrics Instrumentation

#### Problem
Supervising delivery success rates, dispatch durations, dead-letter escalations, and inbound validation outcomes in real time within Prometheus, Grafana, or Datadog.

#### Solution
Subscribe the [`ActivitySource`](../src/EricksonLopez.Webhooks/WebhookDiagnostics.cs) and [`Meter`](../src/EricksonLopez.Webhooks/WebhookDiagnostics.cs) defined in [`WebhookDiagnostics.DiagnosticSourceName`](../src/EricksonLopez.Webhooks/WebhookDiagnostics.cs).

#### Complete Implementation
```csharp
using EricksonLopez.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(WebhookDiagnostics.DiagnosticSourceName);
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(WebhookDiagnostics.DiagnosticSourceName);
    });
```
