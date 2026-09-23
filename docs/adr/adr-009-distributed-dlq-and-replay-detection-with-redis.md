# ADR-009: Distributed Dead-Letter Queue and Anti-Replay Detection via Redis

## Status
Accepted

## Date
2026-09-05

## Context
In single-instance or testing environments, `InMemoryWebhookDeadLetterSink` and `InMemoryWebhookReplayDetector` provide simple, zero-dependency in-process mechanisms for dead-letter queuing and replay attack mitigation.

However, in multi-replica enterprise deployments (e.g. Kubernetes, AWS ECS, Azure Container Apps):
1. **State Isolation**: A replay attack directed at Pod B cannot be detected by Pod A if anti-replay state resides solely in process memory.
2. **DLQ Ephemerality**: If a container crashes, terminates, or restarts, all entries in an in-memory DLQ are permanently lost, violating audit and non-destructive failure handling invariants.
3. **Memory Pressure**: High failure rates against offline third-party endpoints could overwhelm local container memory limits if DLQ items accumulate.

## Decision
We introduce the `EricksonLopez.Webhooks.Redis` provider library, offering distributed, durable implementations of core contracts backed by `StackExchange.Redis`:

1. **`RedisWebhookDeadLetterSink`**:
   - Implements `IWebhookDeadLetterSink`.
   - Serializes terminal delivery failures (`DeadLetterEnvelope`) to Redis Lists via `ListRightPushAsync` using compile-time source-generated `JsonSerializerContext` (`DeadLetterJsonContext`), guaranteeing 100% Native AOT compatibility.
   - Ensures persistent, durable dead-letter storage shared across all dispatching worker nodes.
2. **`RedisWebhookReplayDetector`**:
   - Implements `IWebhookReplayDetector`.
   - Evaluates inbound webhook identifiers (`webhook-id` or `X-Webhook-Delivery-Id`) using atomic Redis operations (`StringSetAsync(key, value, expiry, When.NotExists)`).
   - Enforces key expiration (TTL) matching the configured `TimestampTolerance` window, ensuring automatic memory reclamation.
   - Neutralizes distributed replay attacks regardless of which receiver replica handles the incoming HTTP request.
3. **Dependency Injection Extensions**:
   - Provides fluent extension methods `AddRedisWebhookDeadLetterSink` and `AddRedisWebhookReplayDetector` on `IServiceCollection`.

## Consequences

### Positive
- Fully distributed, production-grade anti-replay protection across clustered ASP.NET Core ingress gateways.
- Durable, multi-instance dead-letter queue storage surviving process restarts and rolling container deployments.
- Zero reflection; all JSON serialization utilizes compile-time source generators for complete Native AOT trimming safety.
- Decoupled architecture: consumers only pull the Redis package if their deployment topology requires distributed persistence.

### Negative
- Requires a functioning Redis instance or cluster in production environments.
- Network latency to Redis must be accounted for during inbound request processing (typically < 1 ms in cloud VPCs).

## References
- [ADR-001: Webhook Delivery Model](adr-001-webhook-delivery-model.md)
- [ADR-002: Package Existence & Invariant Justification](adr-002-package-existence-justification.md)
- [ADR-003: Subscription Management Scope Boundary](adr-003-subscription-management-scope-boundary.md)
