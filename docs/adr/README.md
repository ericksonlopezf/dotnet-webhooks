# Architectural Decision Records (ADRs) — EricksonLopez.Webhooks

This directory contains the formal, immutable record of architectural design decisions established for `EricksonLopez.Webhooks` and its extension packages. Each record codifies invariants, guarantees, and scope boundaries.

## Sequential Decision Index

| ADR | Title | Status | Date | Summary of Invariants & Scope Boundaries |
|:---:|---|:---:|:---:|---|
| [ADR-001](./adr-001-webhook-delivery-model.md) | Webhook Delivery Model | Accepted | Aug 2026 | Establishes foundational delivery and receiving model: HMAC-SHA256, standard headers (`X-Webhook-*`), constant-time validation, and Native AOT compatibility. |
| [ADR-002](./adr-002-package-existence-justification.md) | Package Existence & Invariant Justification | Accepted (Amended) | Sep 2026 | Formally justified under Principles 14 and 15. Incorporates Invariant 7 (Structured Logging), Invariant 8 (Zero-Downtime Secret Rotation), clarifies DLQ durability, and notes Invariant 3 supersession by ADR-011. |
| [ADR-003](./adr-003-subscription-management-scope-boundary.md) | Subscription Management & Event Cataloging Scope Boundary | Accepted | Sep 2026 | Codifies strict scope limits: permanent rejection of endpoint CRUD, event catalog persistence, and custom pluggable hash schemes in the foundational Tier 0 package. |
| [ADR-004](./adr-004-opentelemetry-observability.md) | OpenTelemetry Observability via ActivitySource and Meter | Accepted | Sep 2026 | Native BCL instrumentation for distributed tracing (`webhook.send`) and quantitative metrics (`webhook.deliveries.total`, `webhook.dead_letter.total`, latency histograms) with zero dependencies. |
| [ADR-005](./adr-005-dual-protocol-header-compatibility.md) | Dual-Protocol Header Compatibility | Accepted | Sep 2026 | Dual-protocol architecture: concurrent support for enterprise `X-Webhook-*` and StandardWebhooks (`webhook-*`) headers with auto-detection on receipt. |
| [ADR-006](./adr-006-performance-benchmarking-and-allocation-budgets.md) | Performance Benchmarking Harness & Allocation Budgets | Accepted | Sep 2026 | Automated performance benchmarking harness via BenchmarkDotNet ensuring memory budgets and zero-allocation hot paths. |
| [ADR-007](./adr-007-minimal-apis-endpoint-filter.md) | ASP.NET Core Minimal APIs Endpoint Filter Integration | Accepted | Sep 2026 | Native Minimal APIs endpoint filter (`WebhookEndpointFilter`) and route builder extensions (`RequireWebhookSignature`) with typed JSON rejections. |
| [ADR-008](./adr-008-zero-allocation-span-signature-verification.md) | Zero-Allocation Span-Based Candidate Tokenization in Signature Verification | Accepted | Sep 2026 | Eliminates string.Split heap allocations via ReadOnlySpan tokenization and introduces a defensive 5-candidate ceiling against CPU DoS attacks. |
| [ADR-009](./adr-009-distributed-dlq-and-replay-detection-with-redis.md) | Distributed Dead-Letter Queue and Anti-Replay Detection via Redis | Accepted | Sep 2026 | Introduces `EricksonLopez.Webhooks.Redis` provider with persistent Redis lists DLQ (`ListRightPushAsync`) and distributed idempotency replay detector. |
| [ADR-010](./adr-010-ssrf-defense-and-dns-rebinding-mitigation.md) | Server-Side Request Forgery (SSRF) Defense and DNS Rebinding Mitigation | Accepted | Sep 2026 | Integrates `SafeSocketsHttpHandlerFactory` and connection-time IP validation to neutralize SSRF and DNS rebinding TOCTOU attacks. |
| [ADR-011](./adr-011-standard-resilience-handler-delegation.md) | Outbound Resilience Delegation via Microsoft.Extensions.Http.Resilience | Accepted | Sep 2026 | Amends ADR-002 Invariant 3: delegates outbound retry policies, rate-limiting, and backoffs to Microsoft.Extensions.Http.Resilience (Polly v8) via AddStandardResilienceHandler, preserving WebhookSender as a pure single-dispatch engine. |

---

## ADR Governance in the EricksonLopez Ecosystem

1. **Autonomous Local Sequence**: Each library in the ecosystem maintains its own sequential ADR catalog starting at `ADR-001`, encapsulating its specific design invariants and operational contracts.
2. **Immutability & Amendments**: Accepted ADRs are immutable. If design requirements evolve, the record is either explicitly amended with historical context or superseded by a subsequent ADR.
3. **Invariant-First Doctrine**: Architectural decisions are grounded in non-negotiable security, functional correctness, determinism, and performance guarantees rather than superficial convenience features.
4. **Compliance Audits**: Every pull request and new capability must strictly comply with the invariants ratified in this catalog.
