# Architecture Guide — EricksonLopez.Webhooks

This document describes the internal architecture, design principles, layer topology, interaction sequence diagrams, and delivery state machines of `EricksonLopez.Webhooks`.

---

## 1. Layer Topology & Ecosystem Architecture

```mermaid
graph TD
    subgraph Consumers ["Application / Consumer Layer"]
        App["ASP.NET Core Web App / Minimal APIs"]
        Worker["Transactional Outbox Dispatcher Worker"]
    end

    subgraph AspNetCore ["EricksonLopez.Webhooks.AspNetCore"]
        Filter["WebhookEndpointFilter"]
        Middleware["WebhookReceiverMiddleware"]
        Validator["WebhookValidator (IWebhookValidator)"]
        OptionsRx["WebhookReceiverOptions"]
    end

    subgraph Core ["EricksonLopez.Webhooks (Core Engine)"]
        Sender["WebhookSender (IWebhookSender)"]
        Signer["WebhookSigner"]
        Headers["WebhookHeaders"]
        Secret["WebhookSecret"]
        Msg["WebhookMessage & WebhookPayload"]
        Diagnostics["WebhookDiagnostics (ActivitySource & Meter)"]
        Errors["WebhookErrors (Result<T>)"]
        DLQContract["IWebhookDeadLetterSink"]
        ReplayContract["IWebhookReplayDetector"]
        SafeHandler["SafeSocketsHttpHandlerFactory"]
    end

    subgraph Infrastructure ["EricksonLopez.Webhooks.Redis (Provider)"]
        RedisDLQ["RedisWebhookDeadLetterSink"]
        RedisReplay["RedisWebhookReplayDetector"]
        RedisEnvelope["DeadLetterEnvelope & DeadLetterJsonContext"]
    end

    App --> Filter
    App --> Middleware
    Filter --> Validator
    Middleware --> Validator
    Validator --> Signer
    Validator --> ReplayContract
    Validator --> OptionsRx

    Worker --> Sender
    Sender --> SafeHandler
    Sender --> Signer
    Sender --> DLQContract
    Sender --> Diagnostics
    Sender --> Errors

    RedisDLQ -.->|Implements| DLQContract
    RedisReplay -.->|Implements| ReplayContract
```

---

## 2. Outbound Dispatch Sequence Diagram

```mermaid
sequenceDiagram
    autonumber
    participant App as Application / Outbox Worker
    participant Sender as WebhookSender
    participant Safe as SafeSocketsHttpHandlerFactory
    participant Signer as WebhookSigner
    participant Remote as Remote HTTP Receiver
    participant DLQ as IWebhookDeadLetterSink

    App->>Sender: SendAsync(WebhookMessage, CancellationToken)
    Sender->>Sender: Preflight SSRF & URL Scheme Check (http/https)
    alt Insecure Scheme or Loopback/Private IP Blocked
        Sender-->>App: Result.Failure(WebhookErrors.SecurityViolation)
    else Target URL Authorized
        Sender->>Signer: ComputeSignature(secret, timestamp, payload, protocol, webhookId)
        Signer-->>Sender: Signature string (e.g. "v1,abc..." or "v1=hex...")
        Sender->>Safe: Socket Connection Request
        Safe->>Remote: TCP Connect (Connection-Time IP Validation against DNS Rebinding)
        alt Remote Returns HTTP 2xx
            Remote-->>Sender: HTTP 200 OK (ResponseHeadersRead)
            Sender-->>App: Result.Success(WebhookDeliveryResult)
        else HTTP 4xx/5xx or Retry Exhaustion
            Remote-->>Sender: Terminal Failure (or Timeout)
            Sender->>DLQ: EnqueueAsync(targetUrl, payload, lastResult)
            Sender-->>App: Result.Failure(WebhookErrors.DeliveryFailed)
        end
    end
```

---

## 3. Inbound Ingress Sequence Diagram (Minimal APIs & Middleware)

```mermaid
sequenceDiagram
    autonumber
    participant Client as External Sender / SaaS Webhook
    participant Pipeline as ASP.NET Core Request Pipeline
    participant Filter as WebhookEndpointFilter
    participant Validator as WebhookValidator
    participant Signer as WebhookSigner
    participant Replay as IWebhookReplayDetector
    participant Endpoint as Route Handler Delegate

    Client->>Pipeline: HTTP POST /api/webhooks
    Pipeline->>Filter: InvokeAsync(context, next)
    Filter->>Validator: ValidateRequestAsync(httpContext)
    
    Validator->>Validator: 1. Verify Content-Length <= MaxPayloadSizeBytes
    alt Size Limit Exceeded
        Validator-->>Filter: Result.Failure(WebhookErrors.PayloadTooLarge)
        Filter-->>Client: HTTP 413 Payload Too Large
    else Size Valid
        Validator->>Validator: 2. Verify Timestamp Freshness vs TimeProvider
        alt Clock Skew > TimestampTolerance
            Validator-->>Filter: Result.Failure(WebhookErrors.SecurityViolation)
            Filter-->>Client: HTTP 401 Unauthorized
        else Timestamp Fresh
            Validator->>Signer: VerifySignatureAsync(keys, timestamp, stream, signature)
            alt Signature Mismatch (FixedTimeEquals == false)
                Signer-->>Validator: false
                Validator-->>Filter: Result.Failure(WebhookErrors.SecurityViolation)
                Filter-->>Client: HTTP 401 Unauthorized
            else Signature Valid
                Signer-->>Validator: true
                Validator->>Replay: TryRecordAsync(webhookId)
                alt Replay Detected (ID Already Processed)
                    Replay-->>Validator: false
                    Validator-->>Filter: Result.Failure(WebhookErrors.SecurityViolation)
                    Filter-->>Client: HTTP 401 Unauthorized
                else ID Unique & Accepted
                    Replay-->>Validator: true
                    Validator-->>Filter: Result.Success(true)
                    Filter->>Endpoint: next(context)
                    Endpoint-->>Client: HTTP 200 OK
                end
            end
        end
    end
```

---

## 4. Outbound Delivery Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> Initialized: WebhookMessage Instantiated
    Initialized --> SecurityPreflight: SendAsync Invoked
    
    SecurityPreflight --> SecurityRejected: SSRF Violation / Private IP / Disallowed Scheme
    SecurityRejected --> [*]: Returns WebhookErrors.SecurityViolation
    
    SecurityPreflight --> PayloadSigning: Target URL Authorized
    PayloadSigning --> DispatchingAttempt: HMAC-SHA256 Computed
    
    DispatchingAttempt --> Success: HTTP 2xx Received
    Success --> [*]: Returns Result.Success(WebhookDeliveryResult)
    
    DispatchingAttempt --> RetryEvaluation: Transient HTTP Error (5xx, 408, 429)
    RetryEvaluation --> ExponentialBackoff: Retries Remaining
    ExponentialBackoff --> DispatchingAttempt: Jitter Delay Elapsed
    
    RetryEvaluation --> TerminalFailure: Max Retries Exhausted or Non-Retryable 4xx
    TerminalFailure --> DeadLetterQueue: EnqueueAsync Invoked
    DeadLetterQueue --> [*]: Returns Result.Failure(WebhookErrors.DeliveryFailed)
```

---

## 5. Layer Responsibilities & Transition Contracts

| Architectural Layer | Core Components | Primary Invariants & Responsibilities | Next Transition Layer |
|---|---|---|---|
| **Ingress Filtering** | `WebhookReceiverMiddleware`, `WebhookEndpointFilter` | Intercept incoming requests on protected routes. Activate non-destructive request body buffering (`request.EnableBuffering()`). | Delegates HTTP context to `IWebhookValidator`. |
| **Cryptographic Validation** | `WebhookValidator`, `WebhookSigner` | Enforce payload size limits, verify timestamp drift against `TimeProvider`, and evaluate HMAC signatures with constant-time `FixedTimeEquals`. | On valid signature, evaluates idempotency in `IWebhookReplayDetector`. |
| **Anti-Replay Protection** | `InMemoryWebhookReplayDetector`, `RedisWebhookReplayDetector` | Atomically record unique message identifiers (`webhook-id` / `X-Webhook-Delivery-Id`) with time-to-live matching tolerance window. | If ID is unique, executes downstream application route delegate. |
| **Outbound Dispatch** | `WebhookSender`, `SafeSocketsHttpHandlerFactory` | Preflight URL authorization, socket-level IP validation against SSRF / DNS rebinding, delegated Polly resilience pipeline. | If delivery fails terminally, routes envelope to DLQ contract. |
| **Dead-Letter Escalation** | `IWebhookDeadLetterSink`, `RedisWebhookDeadLetterSink` | Non-destructive escalation of unrecoverable delivery failures into persistent storage (Redis Lists). | Terminal state; available for operational auditing and replay tooling. |
| **Telemetry & Observability** | `WebhookDiagnostics` | BCL-native W3C `ActivitySource` distributed tracing (`webhook.send`) and `Meter` counters/histograms. Zero allocations when detached. | Consumed by OpenTelemetry OTLP / Prometheus exporters. |
