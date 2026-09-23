# ADR-001: Webhook Delivery Model

## Status
Accepted

## Date
2026-09-04

## Context
Across the EricksonLopez ecosystem, multiple microservices and bounded contexts (e.g., OpusHydra, Payment gateways) require the ability to asynchronously dispatch notifications of domain events to external consumers via HTTP Webhooks. Without a standardized approach, different consumers would implement custom serialization, signing algorithms (HMAC), and dispatch strategies, leading to duplicated effort, inconsistent security implementations, and potential vulnerabilities in payload validation.

We need a unified library (`EricksonLopez.Webhooks`) to standardize how webhooks are signed, structured, and securely transmitted across the network, adhering strictly to our ecosystem rules (Native AOT, zero-allocation).

## Decision
We will extract and formalize `EricksonLopez.Webhooks` as the foundational Tier 0 webhook abstraction library.

1. **Standardized Signing (HMAC-SHA256)**: All webhooks MUST be signed using a standard `WebhookSigner` which calculates an HMAC-SHA256 signature of the payload using a pre-shared secret.
2. **Standardized Headers**: The signature MUST be attached to the HTTP request via a standard header (e.g., `X-Webhook-Signature`), accompanied by a timestamp header (`X-Webhook-Timestamp`) to prevent replay attacks.
3. **Payload Verification**: Consumers of the webhook MUST use the reciprocal `WebhookValidator` (or equivalent abstraction) to validate the signature in constant-time to prevent timing attacks.
4. **Native AOT Compatibility**: The serialization and signing mechanisms MUST be fully compatible with Native AOT, avoiding dynamic reflection and relying on `System.Text.Json` source generators.

## Consequences

### Positive
- **Security**: Uniform, cryptographically secure signature generation and validation prevents spoofing and tampering.
- **Maintainability**: Centralized webhook logic eliminates boilerplate across different microservices.
- **Resilience**: The library can be easily combined with `EricksonLopez.Idempotency` and `EricksonLopez.Outbox` to ensure reliable delivery.

### Negative
- **Dependency**: Microservices must depend on `EricksonLopez.Webhooks` and correctly configure the signing keys via safe configuration mechanisms (e.g., Azure Key Vault).

## Compliance
- This decision must be enforced across all consumer applications (like `OpusHydra`). Legacy custom webhook implementations must be migrated to this package.

## References
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [ADR-005: Dual-Protocol Header Compatibility](./adr-005-dual-protocol-header-compatibility.md)
