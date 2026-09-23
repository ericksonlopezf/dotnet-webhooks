# ADR-006: Performance Benchmarking Harness and Memory Allocation Budgets

## Status
Accepted

## Date
2026-09-04

## Context
EricksonLopez Tier 0 packages require formal, verifiable performance and memory allocation budgets. Assertions of "zero-allocation" or "high-performance" must be substantiated by automated benchmarks run under controlled conditions using industry-standard tooling (BenchmarkDotNet).

## Decision
We introduce a dedicated benchmark harness `benchmarks/EricksonLopez.Webhooks.Benchmarks` integrated into the repository solution:

1. **Benchmark Tooling**: Utilize BenchmarkDotNet with memory diagnosers enabled (`[MemoryDiagnoser]`).
2. **Allocation Budgets**:
   - `WebhookSigner.VerifySignature`: 0 allocated bytes on heap for verification comparisons (excluding input buffers).
   - `WebhookSender.SendAsync`: Zero heap allocations from logging delegates (enforced via `[LoggerMessage]`) and zero boxing when OpenTelemetry is inactive. Measured by `WebhookSenderBenchmarks.SendAsync_Success` under `MaxRetries=0` with a no-op HTTP handler.
   - `InMemoryWebhookDeadLetterSink.EnqueueAsync`: Zero GC pressure on steady-state thread-safe queuing.
3. **CI/Release Verification**: Benchmarks are tracked across releases to catch micro-regressions before publishing NuGet packages.

## Consequences

### Positive
- Transparent, verifiable metrics defending performance claims against competitors.
- Continuous regression detection for memory allocations and throughput bottlenecks.

### Negative
- Adds BenchmarkDotNet project dependency to the repository build tree (isolated in `benchmarks/`).

## References
- [Product Strategy](../product-strategy.md) (Initiative 10)
- [Directory.Packages.props](../../Directory.Packages.props)

