# Quick Start — EricksonLopez.Webhooks

Integrate resilient outbound webhook dispatching and cryptographically verified inbound webhook reception into your ASP.NET Core application in under 5 minutes.

---

## 1. Installation

Install the required packages from NuGet:

```bash
# Core delivery engine, HMAC signer, and resilience
dotnet add package EricksonLopez.Webhooks

# ASP.NET Core ingress middleware and Minimal APIs endpoint filters
dotnet add package EricksonLopez.Webhooks.AspNetCore

# (Optional) Distributed Redis DLQ and replay detector
dotnet add package EricksonLopez.Webhooks.Redis
```

---

## 2. Resilient Outbound Dispatch

### Step A: Register the Dispatcher
In `Program.cs`, register `IWebhookSender`:

```csharp
using EricksonLopez.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebhooks(options =>
{
    options.Timeout = TimeSpan.FromSeconds(10);
    options.MaxPayloadSizeBytes = 10 * 1024 * 1024;
    options.EnableSsrfProtection = true; // Connection-time IP validation against SSRF
});
```

### Step B: Dispatch a Webhook
Inject `IWebhookSender` into your controller, background service, or Minimal API endpoint:

```csharp
app.MapPost("/api/orders/checkout", async (IWebhookSender sender) =>
{
    var message = new WebhookMessage(
        new Uri("https://partner.example.com/webhooks"),
        new WebhookSecret("whsec_live_secret_key_1234567890"),
        "order.completed",
        $"evt_{Guid.NewGuid():N}",
        "{\"orderId\":\"ord_9921\",\"amount\":99.50}");

    var result = await sender.SendAsync(message);

    return result.IsSuccess
        ? Results.Ok(new { status = "delivered", statusCode = result.Value.StatusCode })
        : Results.Problem(result.Error.Description, statusCode: 502);
});
```

---

## 3. Secure Inbound Webhook Reception in ASP.NET Core

### Step A: Register Inbound Validator & Options
In `Program.cs`, configure `AddWebhookReceiver`:

```csharp
using EricksonLopez.Webhooks.AspNetCore;

builder.Services.AddWebhookReceiver(options =>
{
    options.SecretKey = builder.Configuration["Webhooks:ReceiverSecret"] ?? "whsec_shared_secret_key";
    options.TimestampTolerance = TimeSpan.FromMinutes(5); // Reject replays drifting beyond 5 min
    options.MaxPayloadSizeBytes = 2 * 1024 * 1024;        // 2 MB maximum body limit
});

// Register single-node in-memory replay detector
builder.Services.AddInMemoryReplayDetector(TimeSpan.FromMinutes(5));
```

### Step B: Protect Inbound Webhook Routes
Apply `.RequireWebhookSignature()` to individual routes or route groups:

```csharp
app.MapPost("/api/webhooks/incoming", async (HttpContext context) =>
{
    // The request body has been verified in constant time and rewound to position 0
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();

    return Results.Ok(new { status = "verified", bytesReceived = body.Length });
}).RequireWebhookSignature();
```

If the signature header is missing, corrupted, expired, or replayed, the endpoint filter intercepts the request and responds with typed JSON error responses (HTTP 401 Unauthorized or HTTP 413 Payload Too Large) before user code executes.
