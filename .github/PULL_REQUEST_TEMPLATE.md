## Description
<!-- Provide a clear, concise summary of the changes introduced in this pull request and the problem they solve. -->

## Related Issues & ADRs
<!-- Link to open issues or new/amended ADRs in docs/adr/ (e.g. Closes #12, References ADR-007). -->
- Closes #
- References ADR: 

## Packages Affected
<!-- Select all packages affected by this change. -->
- [ ] `EricksonLopez.Webhooks`
- [ ] `EricksonLopez.Webhooks.AspNetCore`
- [ ] `EricksonLopez.Webhooks.Redis`
- [ ] `EricksonLopez.Webhooks.Benchmarks`
- [ ] `EricksonLopez.Webhooks.Sample`
- [ ] Documentation / CI / Build tooling

## Type of Change
<!-- Select the appropriate change type. -->
- [ ] Bug fix (non-breaking change fixing an issue)
- [ ] New feature (non-breaking change adding functionality)
- [ ] Breaking change (fix or feature causing existing functionality to change)
- [ ] Performance optimization / Zero-allocation improvement
- [ ] Documentation update

## Architectural Invariants & Quality Gates Checklist
<!-- Verify that all repository quality standards and gates are satisfied. -->
- [ ] **Compilation**: Solution compiles cleanly across all target frameworks (`net8.0`, `net9.0`, `net10.0`) with `TreatWarningsAsErrors=true`.
- [ ] **Tests**: Unit and integration tests pass locally (`dotnet test EricksonLopez.Webhooks.slnx -c Release`).
- [ ] **Repository Compliance**: Compliance verification passes (`pwsh ./scripts/validate-compliance.ps1`).
- [ ] **Formatting**: Code adheres to `.editorconfig` formatting (`dotnet format --verify-no-changes`).
- [ ] **Native AOT**: Changes do not introduce dynamic reflection, runtime code generation, or trimming warnings (`EnableTrimAnalyzer=true`).
- [ ] **Mutation Testing**: Stryker mutation score remains at or above the 95% threshold (`dotnet stryker --config-file stryker-config.json`).
- [ ] **Benchmark Invariant**: Zero-allocation hot path invariant is preserved (0 B allocated on cryptographic and dispatch hot paths).
- [ ] **Documentation**: All public APIs include XML documentation comments, and relevant guides under `/docs/` have been updated in English.
- [ ] **Changelog**: `CHANGELOG.md` has been updated under `[Unreleased]` following Keep a Changelog conventions.
