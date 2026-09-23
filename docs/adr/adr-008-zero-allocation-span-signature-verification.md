# ADR-008: Zero-Allocation Span-Based Candidate Tokenization in Webhook Signature Verification

## Status
Accepted

## Date
2026-09-05

## Context
Under the StandardWebhooks specification (and during dual-key secret rotation), incoming webhook requests may transmit multiple candidate signatures within a single HTTP header, separated by whitespace (e.g. `"v1,sig1 v1,sig2"`).

In high-throughput webhook ingestion engines, splitting signature strings via `string.Split(' ')` causes continuous GC heap allocations (`string[]` and sliced `string` instances). Furthermore, processing an unbounded number of space-delimited candidate signatures exposes receivers to algorithmic complexity and CPU exhaustion attacks (Linear CPU DoS via Signature Candidate Explosion), where an adversarial sender includes hundreds of invalid signatures causing repeated HMAC calculations.

## Decision
We refactor `WebhookSigner.VerifySignature` to eliminate heap allocations and enforce a candidate ceiling:

1. **Zero-Allocation Tokenization**: Signature candidates are extracted iteratively using `ReadOnlySpan<char>` slicing and `IndexOf(' ')` over the raw header value span.
2. **Defensive Candidate Boundary**: Verification terminates and returns `false` immediately if more than 5 candidate signatures (`candidateCount > 5`) are encountered in a single header.
3. **Span-Based Digest Validation**: Candidate prefixing and HMAC verification operate directly on character and byte spans, avoiding intermediary string materialization.

## Consequences

### Positive
- Zero heap allocations (`0 B`) during multi-candidate signature extraction and parsing.
- Deterministic mitigation of candidate explosion CPU exhaustion attacks.
- Preserves full compliance with the StandardWebhooks specification and zero-downtime key rotation workflows.

### Negative
- Senders attempting key rotation with more than 5 simultaneous active keys will be rejected; 5 keys is well above industry standards (typically 2).

## References
- [ADR-006: Performance Benchmarking Harness and Memory Allocation Budgets](adr-006-performance-benchmarking-and-allocation-budgets.md)
- StandardWebhooks Specification: Dual-Signature Verification
