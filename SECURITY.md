# Security Policy

## Supported Versions

We actively provide security patches and updates for the following versions of `EricksonLopez.Webhooks`, `EricksonLopez.Webhooks.AspNetCore`, and `EricksonLopez.Webhooks.Redis`:

| Version | Supported          | Security Maintenance Status |
| :---: | :---: | :--- |
| **2.0.x** | :white_check_mark: | **Active Development** (Current stable release) |
| **1.1.x** | :white_check_mark: | **Maintenance Mode** (Critical security fixes only) |
| **1.0.x** | :x: | **End of Life** (Superseded by 1.1.x and 2.0.x) |
| **< 1.0** | :x: | **Unsupported** |

---

## Reporting a Vulnerability

Security is a foundational design pillar of `EricksonLopez.Webhooks`. The engine implements cryptographic signature verification, timing side-channel immunity, anti-replay clock drift tolerance, zero-trust network perimeter defense against SSRF, and multi-secret rotation.

If you identify or suspect a security vulnerability (such as a cryptographic defect, timing side-channel leak, anti-replay bypass, SSRF bypass, or denial-of-service vector), **please report it privately**.

### Disclosure Channels
* **Security Contact Email**: [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)
* **Subject Line**: `[SECURITY] Potential vulnerability in EricksonLopez.Webhooks`

### What to Include in Your Report
To enable rapid evaluation and coordinated patching, please provide:
1. **Affected Component**: Specify the package (`EricksonLopez.Webhooks`, `EricksonLopez.Webhooks.AspNetCore`, or `EricksonLopez.Webhooks.Redis`) and target framework (`net8.0`, `net9.0`, or `net10.0`).
2. **Vulnerability Classification**: (e.g. SSRF evasion, timing variance, algorithmic complexity DoS, secret leakage).
3. **Reproduction Proof-of-Concept (PoC)**: Minimal, self-contained C# code snippet or HTTP request sequence demonstrating the issue.
4. **Impact Assessment**: Technical explanation of the potential exploitability and blast radius.
5. **Suggested Mitigation**: (Optional) Remediation ideas or patch proposals.

### Response & Remediation SLA
* **Initial Acknowledgement**: Within 24–48 hours.
* **Triage & Severity Assessment**: Within 5 business days.
* **Coordinated Disclosure**: A patched release and security advisory will be coordinated and published before public disclosure.

Please do not open public GitHub issues, discussions, or pull requests regarding suspected security vulnerabilities until a coordinated resolution has been published.

---

## Supply Chain Security

`EricksonLopez.Webhooks` implements enterprise-grade supply chain defense mechanisms:

1. **Strong Name Signing**: All production assemblies are strongly named using an RSA-2048 signing key (`EricksonLopez.snk`). Public key identity is verified via `Directory.Build.props`.
2. **Deterministic Builds & SourceLink**: Every package embeds compiler flags (`PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`) and integrates `Microsoft.SourceLink.GitHub` to bind compiled binaries deterministically to verifiable Git commits.
3. **Symbol Packages (`.snupkg`)**: Debugging symbols are published alongside NuGet packages to allow audited step-through debugging of the exact source code.
4. **Automated Vulnerability Audits**: Continuous integration pipelines execute `dotnet list package --vulnerable --include-transitive` and GitHub CodeQL static security analysis weekly and on every pull request.

---

## Known Security Boundaries & Threat Model

The following defensive invariants define the security perimeter of the library:

| Security Boundary | Threat Mitigated | Defensive Mechanism |
|---|---|---|
| **Timing Side-Channels** | Byte-by-byte HMAC secret reconstruction via microsecond response latency variance. | `CryptographicOperations.FixedTimeEquals` over UTF-8 byte spans evaluated across all candidate signatures. |
| **Replay Attacks** | Capturing valid webhook requests over the public internet and replaying them to duplicate state changes. | Strict timestamp tolerance window (`TimestampTolerance`, default: 5 minutes) evaluated against `TimeProvider`, combined with `IWebhookReplayDetector`. |
| **Server-Side Request Forgery (SSRF)** | Malicious webhook target URLs targeting AWS/GCP metadata endpoints (`169.254.169.254`), loopback (`127.0.0.1`), or private subnets (RFC 1918). | Zero-trust IP validation and DNS resolution defense via `SafeSocketsHttpHandlerFactory` and `EricksonLopez.Security.Network`, neutralizing DNS rebinding TOCTOU attacks. |
| **Algorithmic Complexity DoS** | Malicious HTTP requests providing hundreds of space-delimited signatures to force high CPU hashing loops. | Hard ceiling of 5 candidate tokens per signature header in `WebhookValidator`. Excess candidates trigger immediate rejection. |
| **Unbounded Remote Response Buffering** | Remote webhook targets returning gigabyte-sized infinite response streams to exhaust sender memory. | `WebhookSender` dispatches requests using `HttpCompletionOption.ResponseHeadersRead` and disposes `HttpResponseMessage` immediately without buffering the response body. |
| **Memory Exhaustion via DLQ Flooding** | Adversaries generating high-volume failures to exhaust in-memory dead-letter queue storage. | `InMemoryWebhookDeadLetterSink` enforces a strict bounded capacity (default: 1,000 items) with an atomic `DropOldest` eviction policy under pressure. Production workloads are instructed to use `RedisWebhookDeadLetterSink`. |
| **Stream Drain in ASP.NET Core** | Pre-reading `HttpRequest.Body` for HMAC validation destroying downstream model binding streams. | `WebhookReceiverMiddleware` activates `request.EnableBuffering()` and resets `Body.Position = 0` on completion. |
| **Secret Leaks in Logs & Dumps** | Accidentally serializing plain-text webhook secrets in logs or APM diagnostics. | `WebhookSecret` record struct explicitly overrides `ToString()` to return `"[REDACTED]"`. |
