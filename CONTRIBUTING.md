# Contributing to EricksonLopez.Webhooks

Thank you for contributing to `EricksonLopez.Webhooks`! This repository houses foundational, enterprise-grade primitives for outbound webhook delivery and inbound ASP.NET Core verification across the modern .NET ecosystem.

Before contributing, please review this guide to understand our architectural principles, engineering standards, and quality gates.

---

## 1. Architectural Invariants First

All contributions must strictly adhere to the non-negotiable core invariants of the library:

1. **Timing Side-Channel Immunity**: Constant-time signature verification via `CryptographicOperations.FixedTimeEquals` evaluated across all candidate signatures without early bail-out variances.
2. **Anti-Replay Timestamp Defense**: Enforcing clock skew boundaries against injected `TimeProvider` abstractions.
3. **SSRF Network Perimeter Defense**: Preventing Server-Side Request Forgery via `SafeSocketsHttpHandlerFactory` and `EricksonLopez.Security.Network`.
4. **Scope Boundaries ([ADR-003](docs/adr/adr-003-subscription-management-scope-boundary.md))**: Subscription management, endpoint CRUD, and database persistence belong to application-specific domains, not to foundational transport primitives.
5. **Railway-Oriented Programming**: Public APIs leverage functional `Result<T>` and `Result<bool>` from `EricksonLopez.Result` to avoid exception-driven control flow.
6. **Native AOT & Trimming**: Zero dynamic reflection; all types and serialization pipelines must compile cleanly under `EnableTrimAnalyzer=true` and `IsAotCompatible=true`.

---

## 2. Development Prerequisites & Setup

### Prerequisites
* **.NET SDKs**: .NET 8.0, .NET 9.0, and .NET 10.0 SDKs (multi-targeting `net8.0;net9.0;net10.0`).
* **PowerShell**: PowerShell 7+ (`pwsh`) for running compliance audit scripts.
* **Stryker.NET**: Install globally if running mutation tests locally:
  ```bash
  dotnet tool install -g dotnet-stryker
  ```

### Clone and Build
Clone the repository and compile using the solution file:

```bash
git clone https://github.com/ericksonlopezf/dotnet-webhooks.git
cd dotnet-webhooks
dotnet restore EricksonLopez.Webhooks.slnx
dotnet build EricksonLopez.Webhooks.slnx -c Release --no-restore
```

---

## 3. Verifying Quality Gates Locally

Continuous Integration enforces strict gates on every pull request. Run the following checks locally before submitting your PR:

### A. Run Tests & Code Coverage
Execute the test suite with Coverlet data collection:
```bash
dotnet test EricksonLopez.Webhooks.slnx -c Release --no-build \
  --settings coverlet.runsettings \
  --collect:"XPlat Code Coverage"
```

### B. Code Formatting Verification
Ensure all files adhere to `.editorconfig` rules:
```bash
dotnet format --verify-no-changes --verbosity diagnostic
```

### C. Architecture & Compliance Audit
Run the automated compliance validator script:
```powershell
pwsh -File ./scripts/validate-compliance.ps1
```

### D. Stryker.NET Mutation Testing
Verify that your changes maintain the **>= 95% mutation score threshold**:
```bash
cd tests/EricksonLopez.Webhooks.Tests
dotnet stryker --config-file stryker-config.json --concurrency 2 --break-at 95
```

### E. Performance Benchmarks & Zero-Allocation Invariants
Execute the BenchmarkDotNet harness to verify memory allocation budgets:
```bash
dotnet run --project benchmarks/EricksonLopez.Webhooks.Benchmarks/EricksonLopez.Webhooks.Benchmarks.csproj \
  -c Release \
  -f net10.0 -- \
  --filter "*" --job short --exporters json --memory --artifacts ./benchmarks/pr-results
```

Evaluate regression against the baseline:
```powershell
pwsh -File ./scripts/verify-benchmark-gate.ps1 \
  -ReportDir ./benchmarks/pr-results \
  -BaselinePath ./benchmarks/results/baseline.json \
  -MaxLatencyRegressionPercent 5.0
```

---

## 4. Engineering Standards

### C# Coding Invariants
* **File-Scoped Namespaces**: Always use file-scoped namespace declarations (e.g. `namespace EricksonLopez.Webhooks;`).
* **One Type Per File**: Enforce exactly one public or internal top-level type per C# file.
* **Immutability & Safety**: Default to `sealed class`, `readonly record struct`, or `sealed record`.
* **Zero-Allocation Logging**: Use `[LoggerMessage]` source-generated partial methods. Never use string interpolation in log statements.
* **Time Determinism**: Never call `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly. Always inject or utilize `TimeProvider`.
* **Standard MIT Header**: Every C# file must begin with:
  ```csharp
  // Copyright © Erickson Lopez. MIT License.
  ```

### Commit & Branch Conventions
* **Branch Strategy**: Branch from `main` using descriptive prefixes:
  * `feature/your-feature-name`
  * `fix/issue-description`
  * `perf/optimization-name`
  * `docs/documentation-update`
* **Commit Messages**: Follow [Conventional Commits](https://www.conventionalcommits.org/):
  * `feat(webhooks): add distributed replay detector`
  * `fix(aspnetcore): correct candidate tokenization bounds`
  * `perf(crypto): eliminate string allocation on signature verification`
  * `docs(adr): add ADR-009 for Redis distributed DLQ`

---

## 5. Pull Request Workflow

1. Create a feature or bugfix branch.
2. Ensure all tests compile and pass across `net8.0`, `net9.0`, and `net10.0`.
3. Verify that `./scripts/validate-compliance.ps1` passes with zero violations.
4. Update `CHANGELOG.md` under the `[Unreleased]` section following [Keep a Changelog](https://keepachangelog.com/).
5. If introducing a major design shift or modifying an invariant, create a new ADR in `docs/adr/` (e.g., `adr-011-decision-name.md`) and register it in `docs/adr/README.md`.
6. Complete the pull request template checklist (`.github/PULL_REQUEST_TEMPLATE.md`).

---

## 6. Community & Policies

* **Code of Conduct**: Review and follow our [Code of Conduct](CODE_OF_CONDUCT.md).
* **Security Reporting**: Report vulnerabilities responsibly via [SECURITY.md](SECURITY.md).
* **Support Channels**: For questions and community discussions, see [SUPPORT.md](SUPPORT.md).
* **License**: Contributions are licensed under the [MIT License](LICENSE).
