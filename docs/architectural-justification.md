# Architectural Justification: EricksonLopez.Webhooks

## 1. Executive Summary & Context

In a modern enterprise ecosystem, systems must communicate with untrusted external third parties (e.g., payment gateways, fiscal authorities, logistics providers, external SaaS partners) via HTTP webhooks. 

A frequent architectural fallacy is assuming that webhooks are "just HTTP requests" that can be handled with raw `HttpClient` calls, or conversely, that they can be replaced by enterprise message brokers (such as RabbitMQ, Kafka, or `EricksonLopez.Messaging`).

`EricksonLopez.Webhooks` exists to establish a **secure, hardened, and resilient boundary between internal enterprise domains and untrusted external HTTP endpoints**.

This document formally justifies the existence of `EricksonLopez.Webhooks` as an autonomous Tier 0 foundational package in accordance with **EricksonLopez Design Principles**:
- **Principle 14**: *Every abstraction must justify its existence.*
- **Principle 15**: *Complexity must be paid for deliberately.*
- **Invariant-First Doctrine**: A package must not be conceptualized around features ("we have a webhook sender"), but around invariants: *"An outbound webhook must guarantee cryptographic integrity, resilient delivery with jitter, and dead-letter capture without blocking threads; an inbound webhook must defend against timing and replay attacks before touching application logic."*

---

## 2. Core Problem Space: The Fallacy of Ad-Hoc Webhooks

Naïve, ad-hoc webhook implementations across services introduce catastrophic vulnerabilities and operational failures:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        AD-HOC WEBHOOK VULNERABILITIES                  │
├───────────────────────────────┬────────────────────────────────────────┤
│ Vulnerability / Failure Mode  │ Production Consequence                 │
├───────────────────────────────┼────────────────────────────────────────┤
│ 1. Timing Side-Channel Attack │ Using standard string comparison       │
│    (Signature Spoofing)       │ (`==` or `.Equals()`) allows attackers │
│                               │ to deduce the HMAC secret byte-by-byte │
│                               │ via execution time analysis.           │
├───────────────────────────────┼────────────────────────────────────────┤
│ 2. Replay Attacks             │ Intercepted valid webhooks can be      │
│    (Duplicate Transactions)   │ re-sent maliciously without timestamp  │
│                               │ expiration and drift tolerance checks. │
├───────────────────────────────┼────────────────────────────────────────┤
│ 3. Thread / Socket Starvation │ Unthrottled retries against slow or    │
│    (Cascading Outages)        │ offline third-party servers tie up     │
│                               │ HTTP connections, leading to starvation│
│                               │ of core internal application workers.  │
├───────────────────────────────┼────────────────────────────────────────┤
│ 4. Silent Data Loss           │ Exhausted retry loops simply log an    │
│    (Unreachable DLQ)          │ error and drop the payload, leaving    │
│                               │ external systems out of synchronization│
│                               │ with no audit trail or retry queue.    │
├───────────────────────────────┼────────────────────────────────────────┤
│ 5. Request Stream Consumption │ In ASP.NET Core, reading the body to   │
│    (Pipeline Incompatibility) │ verify the signature drains the stream,│
│                               │ causing downstream model binders and   │
│                               │ controllers to receive empty payloads. │
└───────────────────────────────┴────────────────────────────────────────┘
```

---

## 3. Fundamental Architectural Invariants

`EricksonLopez.Webhooks` enforces eight critical architectural invariants:

### Invariant 1: Timing-Attack Immunity via Constant-Time Verification
> *Webhook HMAC signature verification MUST execute in constant time regardless of where discrepancies occur.*

`WebhookSigner.VerifySignature` strictly employs `CryptographicOperations.FixedTimeEquals` on UTF-8 byte arrays:
```csharp
// WebhookSigner.cs
if (expectedBytes.Length != receivedBytes.Length)
{
    return false;
}
return CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
```
This guarantees that cryptographic comparisons leak zero timing information, entirely neutralizing side-channel attacks against HMAC secrets.

### Invariant 2: Anti-Replay Defense via Timestamp Drift Tolerance
> *Every inbound webhook MUST carry a Unix timestamp header and MUST be rejected if the timestamp deviates beyond a strict tolerance window.*

Inbound requests verified by `WebhookValidator` enforce `X-Webhook-Timestamp` validation against `TimeProvider.System` (or a mockable time provider in tests). If `(currentTime - requestTime).Duration() > _options.TimestampTolerance` (default: 300 seconds), the request is rejected with `Error.Unauthorized("WebhookValidator.TimestampExpired")`. Intercepted packets cannot be replayed after the tolerance window expires.

### Invariant 3: Resilient Outbound Dispatch with Backoff and Full Jitter
> *Outbound dispatches MUST implement bounded retries with exponential backoff and cryptographic jitter, fast-failing on permanent 4xx errors.*

`WebhookSender` delivers payloads using a randomized jitter algorithm (`RandomNumberGenerator.GetInt32`), preventing "thundering herd" retry waves against recovering third-party servers. Non-retryable client errors (HTTP 400-499, except HTTP 408 Request Timeout and HTTP 429 Too Many Requests) abort immediately, avoiding wasted retries on invalid client URLs or bad credentials.

### Invariant 4: Guaranteed Failure Traceability via Dead-Letter Sinks (DLQ)
> *When maximum retry attempts are exhausted, the failed webhook envelope and delivery diagnostics MUST be deposited into an `IWebhookDeadLetterSink`.*

Failed deliveries are never discarded silently. `WebhookSender` wraps the payload, target URL, attempt count, duration, and HTTP status code into a `WebhookPayload` envelope and routes it to `IWebhookDeadLetterSink.EnqueueAsync`. This enables operator alerting, manual replay, or persistent dead-letter auditing in PostgreSQL or durable storage.

### Invariant 5: Non-Destructive Inbound Stream Buffering
> *Validating an inbound webhook payload MUST NOT prevent downstream ASP.NET Core middleware or endpoints from reading the request body.*

`WebhookValidator` automatically invokes `request.EnableBuffering()` and resets `request.Body.Position = 0` both before and after signature calculation. Downstream controllers, minimal APIs, and JSON deserializers receive the intact request body without conflict.

### Invariant 6: Strict Protocol Separation (Untrusted HTTP vs Internal Messaging)
> *External webhooks MUST NOT be conflated with internal enterprise message buses.*

Internal messaging (`EricksonLopez.Messaging`) operates within trusted network boundaries over AMQP/Kafka with shared schemas and guaranteed broker delivery. Webhooks operate across untrusted public internet infrastructure over raw HTTP with disparate external receivers, asymmetric signing secrets, and differing latency/availability characteristics.

### Invariant 7: Zero-Allocation Structured Logging via Compile-Time Source Generators
> *All outbound webhook lifecycle events MUST emit structured diagnostic logs without heap allocations or boxing.*

`WebhookSender` uses compile-time `[LoggerMessage]` source-generated partial methods for all log events (dispatch, success, non-retryable errors, retry warnings, exhaustion). These delegates avoid boxing, dynamic string interpolation, and parameter array allocations, defaulting safely to `NullLogger<WebhookSender>.Instance` in Native AOT or zero-logging environments.

### Invariant 8: Zero-Downtime Secret Rotation via Multi-Key Verification
> *Inbound webhook verification MUST support concurrent active secret keys to enable scheduled cryptographic rotation without service interruption.*

`WebhookValidator` evaluates all active keys from `WebhookReceiverOptions.SecretKeys` in constant time per key, allowing deployment of a new secret alongside the retired secret. Once external callers have migrated to the new key, the retired key is removed from the list without downtime or failed deliveries.

---

## 4. Component Topology & Layer Allocation

The package is partitioned cleanly between core transport abstractions and ASP.NET Core hosting integration:

```
┌─────────────────────────────────────────────────────────────┐
│                 Consumer Application / Worker               │
│               (OpusHydra Outbox Processor / API)            │
└──────────────────────────────┬──────────────────────────────┘
                               │
               ┌───────────────┴───────────────┐
               │ outbound                      │ inbound
               ▼                               ▼
┌──────────────────────────────┐ ┌──────────────────────────────┐
│    EricksonLopez.Webhooks    │ │ EricksonLopez.Webhooks.      │
│            (Core)            │ │       AspNetCore             │
│                              │ │                              │
│ Contracts:                   │ │ Contracts:                   │
│  - IWebhookSender            │ │  - IWebhookValidator         │
│  - IWebhookDeadLetterSink    │ │                              │
│ Implementations:             │ │ Implementations:             │
│  - WebhookSender             │ │  - WebhookValidator          │
│  - WebhookSigner             │ │  - WebhookReceiverMiddleware │
│  - InMemoryDeadLetterSink    │ │  - WebhookReceiverOptions    │
└──────────────────────────────┘ └──────────────────────────────┘
```

### Layer Permitted Matrix

| Layer / Assembly | Permitted Responsibilities | Strictly Prohibited |
|---|---|---|
| **`EricksonLopez.Webhooks`** | Payload signing, HMAC computation, constant-time verification, outbound HTTP dispatch, backoff & jitter, DLQ contract | ASP.NET Core `HttpContext`, middleware, SQL queries, direct entity mappings |
| **`EricksonLopez.Webhooks.AspNetCore`** | ASP.NET Core middleware, header extraction, request buffering, HTTP pipeline error formatting (`RFC 7807`) | Database connections, outbox polling, external message brokers |

---

## 5. Architectural Comparison & Decision Matrix

| Dimension | Raw `HttpClient` | `Polly` + Raw HTTP | Internal Messaging (`RabbitMQ`) | **`EricksonLopez.Webhooks`** |
|---|---|---|---|---|
| **Constant-Time HMAC** | None (manual) | None (manual) | N/A (broker security) | **Standardized (`FixedTimeEquals`)** |
| **Replay Attack Defense** | None | None | Handled via deduplication | **Timestamp skew validation (`TimeProvider`)** |
| **Cryptographic Jitter** | None | Manual Polly policy | Broker backpressure | **Built-in (`RandomNumberGenerator`)** |
| **Dead-Letter Escalation** | None (silent drop) | None (exception thrown) | DLX / Dead-letter exchange | **`IWebhookDeadLetterSink` contract** |
| **ASP.NET Core Buffering** | Destructive read | Destructive read | N/A | **Non-destructive (`EnableBuffering`)** |
| **Native AOT & Trimming** | Yes | Depends on config | Requires broker drivers | **100% Native AOT & zero reflection** |
| **Domain Portability** | Low (code duplication) | Medium | High (internal only) | **High (dedicated external protocol)** |

---

## 6. Threat Model & Security Analysis

1. **HMAC Key Compromise via Timing Attack**:
   - *Risk*: A malicious consumer sends variations of the signature header and measures nano-second response differences to deduce the secret.
   - *Defense*: `CryptographicOperations.FixedTimeEquals` ensures uniform comparison cycles across all inputs.
2. **Replay of Captured Webhooks (Man-in-the-Middle)**:
   - *Risk*: An attacker intercepts a legitimate webhook payload and replays it hours later to duplicate state transitions (e.g., re-triggering order fulfillment).
   - *Defense*: Clock skew checking rejects any payload with a timestamp exceeding `TimestampTolerance`.
3. **Denial of Service via Slow Third-Party Receivers**:
   - *Risk*: Third-party endpoint takes 30 seconds per request, causing outbound workers to exhaust threads.
   - *Defense*: Strict per-attempt timeouts (`cts.CancelAfter(_options.Timeout)`) and bounded retries.
4. **Forged Webhooks from Impersonators**:
   - *Risk*: Unauthenticated actors POST malicious events to our webhook endpoints.
   - *Defense*: `WebhookReceiverMiddleware` rejects any request lacking a valid HMAC signature matching the configured pre-shared secret key.

---

## 7. Ecosystem Interoperability

`EricksonLopez.Webhooks` works synergistically with other Tier 0 and Tier 1 ecosystem libraries:
- **`EricksonLopez.Outbox`**: Outbox processors dequeue outbound webhook events transactionally and deliver them reliably via `IWebhookSender`.
- **`EricksonLopez.Result`**: Sender and validator return `Result<WebhookDeliveryResult>` and `Result<bool>`, integrating seamlessly into clean architecture handlers.
- **`EricksonLopez.Idempotency`**: Inbound webhook receivers pass validated payloads to idempotency filters to ensure duplicate transmissions from third parties are safely deduped.

---

## 8. Conclusion

`EricksonLopez.Webhooks` fulfills all criteria of Principles 14 and 15. It encapsulates complex cryptographic, anti-replay, and resilient delivery logic into an explicit, secure, and production-tested foundation, completely eliminating insecure ad-hoc HTTP implementations across the enterprise.
