# ADR-003: Subscription Management and Event Cataloging are Application Responsibilities

## Status
Accepted

## Date
2026-09-04

## Context
During competitive parity analysis against platforms such as Svix, WebhookKit, and StandardWebhooks, several features are frequently grouped under the broader umbrella of "webhook management":
1. **Subscription Management**: Storing subscriber endpoint URLs, status flags, secret keys per subscriber, and filtering rules in a relational database.
2. **Event Cataloging**: Maintaining schemas, event types, payload documentation, and developer portals.
3. **Pluggable Signing Algorithms**: Allowing arbitrary cryptographic hash or asymmetric signing algorithms (e.g., RSA, Ed25519, or custom digest schemes).

A proposal was evaluated to determine whether `EricksonLopez.Webhooks` should incorporate an embedded subscription repository, an event registry, and pluggable signature providers.

## Decision
We formally decide that **Subscription Management, Event Cataloging, and Pluggable Signing Algorithms are strictly Application Responsibilities** and will NOT be included in `EricksonLopez.Webhooks`.

Specifically:
1. **No Persistence in Core**: `EricksonLopez.Webhooks` is a Tier 0 foundational library. It must remain decoupled from specific database engines, ORMs (such as Entity Framework Core), and schema migrations. Imposing endpoint tables would force relational dependencies onto non-relational or serverless applications.
2. **Single Cryptographic Standard (HMAC-SHA256)**: The library standardizes exclusively on HMAC-SHA256 with anti-replay timestamp verification. Supporting pluggable signing schemes introduces algorithmic agility vulnerabilities (such as algorithm downgrade attacks) and bloats the library with unnecessary abstractions.
3. **Clean Domain Separation**: Subscriber entities, permissions, billing, tenancy, and event subscription rules belong to the bounded context of the consuming application (e.g., `OpusHydra`), not to the transport and cryptographic engine.
4. **Composition Over Monoliths**: Application workers query their own subscriber repositories, construct target delivery requests, and delegate delivery to `IWebhookSender`.

## Consequences

### Positive
- **Zero Heavy Dependencies**: Core remains strictly dependent only on .NET BCL and foundational abstractions (`EricksonLopez.Result`, `Microsoft.Extensions.Logging.Abstractions`).
- **100% Native AOT & Trimming**: No dynamic ORM queries, reflection-based model builders, or dynamic dispatch are introduced.
- **Architectural Clarity**: Clear boundary between domain logic (who subscribes to what) and infrastructure mechanics (signing, retrying, timestamp validation, buffering).
- **Security Hardening**: Enforces an uncompromised, battle-tested cryptographic standard across all consumers.

### Negative
- Applications must manage their own subscriber persistence layer (or use a higher-tier extension package) to store target URLs and subscriber secrets.

## References
- [ADR-001: Webhook Delivery Model](./adr-001-webhook-delivery-model.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [Competitive Parity Audit](../competitive-parity-audit.md)
- [Product Strategy](../product-strategy.md)

