# Performance & Memory Allocation Guide — EricksonLopez.Webhooks

High-performance engineering directives, low-level cryptographic optimizations, zero-allocation invariants, and memory allocation budgets for `EricksonLopez.Webhooks`.

---

## 1. Memory Allocation Budgets & Hot Path Invariants

`EricksonLopez.Webhooks` is engineered for ultra-high-throughput ingestion and dispatch environments where Garbage Collector (GC) allocations on the hot path directly degrade tail latencies (p99/p99.9).

| Operation / Path | Heap Allocation Budget | Defensive Optimization Technique |
|---|:---:|---|
| **`WebhookSigner.ComputeSignature` (Span)** | **0 B** | `stackalloc byte[32]` for HMAC digests, `ArrayPool<byte>.Shared` for payload spans, and one-shot `HMACSHA256.HashData`. |
| **`WebhookSigner.VerifySignature` (Span)** | **0 B** | `ReadOnlySpan<char>` tokenization, stackalloc binary decoding, and constant-time `FixedTimeEquals`. |
| **Multi-Candidate Signature Tokenization** | **0 B** | Replaced `string.Split(' ')` with iterative span slicing and `IndexOf(' ')`. Bound to 5 candidates max. |
| **Inbound Middleware Body Buffering** | **Bounded** | Reusable `MemoryStream` pooled via ASP.NET Core `request.EnableBuffering()`. Intact stream rewound to position 0. |
| **Outbound Dispatch (`WebhookSender`)** | **Minimal** | Reuses `SocketsHttpHandler`, streams headers via `HttpCompletionOption.ResponseHeadersRead`, and disposes response message immediately. |
| **Dead-Letter Serialization (Redis)** | **0 B reflection** | Zero reflection via compile-time source-generated `JsonSerializerContext` (`DeadLetterJsonContext`). |

---

## 2. Large Object Heap (LOH) Defense

In .NET runtimes, any managed object occupying **85,000 bytes or more** is allocated directly on the Large Object Heap (LOH). The LOH is rarely compacted by default, leading to address space fragmentation, frequent Generation 2 garbage collection pauses, and unpredictable latency spikes.

### Mitigations Built into `EricksonLopez.Webhooks`:
1. **Incremental Stream Hashing**: For multi-megabyte payloads, `WebhookSigner.ComputeSignatureAsync` and `VerifySignatureAsync` consume streams in rented 8,192-byte chunks via `ArrayPool<byte>.Shared.Rent(8192)` and accumulate state through `IncrementalHash.AppendData()`. The payload is never materialized as a contiguous large string in managed heap memory.
2. **Maximum Payload Bounds**: `MaxPayloadSizeBytes` (default: 10 MB) terminates reads immediately if a sender or receiver attempts to stream an excessively large request, thwarting memory denial-of-service attempts.

---

## 3. Algorithmic Complexity & CPU Exhaustion Defense

In the StandardWebhooks specification and multi-key rotation workflows, multiple space-delimited signatures may appear in the `webhook-signature` header (e.g. `v1,sig1 v1,sig2`).

### Defensive Boundaries (ADR-008):
- **Zero-Allocation Tokenization**: Slicing candidate signatures using `ReadOnlySpan<char>` avoids creating temporary `string[]` arrays or substrings.
- **Candidate Count Ceiling**: If a request contains more than **5 candidate tokens**, verification aborts immediately and returns `false`. This protects receivers against algorithmic complexity attacks (Linear CPU DoS via Signature Candidate Explosion) where malicious clients inject hundreds of dummy signatures to consume CPU cycles.

---

## 4. Running BenchmarkDotNet Harnesses

Automated performance benchmarks and memory allocation assertions reside in the [`benchmarks/EricksonLopez.Webhooks.Benchmarks`](../benchmarks/EricksonLopez.Webhooks.Benchmarks) project.

### Executing Micro-Benchmarks
Run the benchmark harness under .NET 10.0 in Release mode:

```bash
dotnet run \
  --project benchmarks/EricksonLopez.Webhooks.Benchmarks/EricksonLopez.Webhooks.Benchmarks.csproj \
  -c Release \
  -f net10.0 -- \
  --filter "*" \
  --job short \
  --exporters json \
  --memory \
  --artifacts ./benchmarks/results
```

### Validating Against the 5% Latency Regression Gate
Compare execution results against the committed performance baseline:

```powershell
pwsh -File ./scripts/verify-benchmark-gate.ps1 `
  -ReportDir ./benchmarks/results `
  -BaselinePath ./benchmarks/results/baseline.json `
  -MaxLatencyRegressionPercent 5.0
```

Continuous integration runs this gate on all pull requests affecting `src/**` and `benchmarks/**`.
