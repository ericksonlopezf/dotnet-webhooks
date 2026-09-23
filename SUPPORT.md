# Support Guide

Thank you for using `EricksonLopez.Webhooks`! This document outlines our support channels, documentation resources, bug reporting guidelines, and enterprise support policies.

---

## Official Documentation & Self-Help

Before opening a ticket or question, please consult the official documentation:

* [**Documentation Master Guide**](docs/README.md) — Four-level reading hierarchy and architecture roadmap.
* [**Quick Start**](docs/quickstart.md) — 5-minute integration guide for outbound dispatch and ASP.NET Core receipt.
* [**Official Cookbook**](docs/cookbook.md) — 10 production-ready recipes covering key rotation, dead-letter queues, and StandardWebhooks.
* [**API Reference**](docs/api-reference.md) — Comprehensive technical reference for all public types, methods, and options.
* [**Troubleshooting Guide**](docs/troubleshooting.md) — Diagnostic steps for signature verification failures, clock skew, SSRF blocks, and stream issues.
* [**Frequently Asked Questions (FAQ)**](docs/faq.md) — Common questions about side-channel immunity, anti-replay defense, and Native AOT.
* [**Architectural Decision Records (ADRs)**](docs/adr/README.md) — Rationale for design decisions and scope boundaries.

---

## Community Support Channels

We offer community-based support through GitHub:

### 1. GitHub Issues (Bugs & Feature Requests)
* **Bug Reports**: If you discover unexpected behavior, signature mismatches, or regression issues, please search [existing issues](https://github.com/ericksonlopezf/dotnet-webhooks/issues) first. If not found, open a [Bug Report](https://github.com/ericksonlopezf/dotnet-webhooks/issues/new?template=bug_report.md).
* **Feature Requests**: If you need new functionality aligned with our architectural invariants, submit a [Feature Request](https://github.com/ericksonlopezf/dotnet-webhooks/issues/new?template=feature_request.md).

### 2. GitHub Discussions (Q&A & Architecture)
* General questions, implementation advice, design patterns, and architecture discussions should be posted on [GitHub Discussions](https://github.com/ericksonlopezf/dotnet-webhooks/discussions).

---

## Security Vulnerabilities

> [!CAUTION]
> **Do not report security vulnerabilities through public GitHub issues or discussions.**

If you suspect a cryptographic vulnerability, timing side-channel defect, anti-replay bypass, or denial-of-service vector:
* Consult our [Security Policy](SECURITY.md).
* Send a private report to [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com) with the subject `[SECURITY] Potential vulnerability in EricksonLopez.Webhooks`.

---

## Response Times & Support Scope

`EricksonLopez.Webhooks` is maintained as an open-source library governed under the MIT License. Support is provided on a best-effort basis by the maintainers and the community:

| Channel | Scope | Target Response |
|---|---|---|
| **Security Reports** | Critical vulnerabilities, cryptographic flaws | 24–48 hours |
| **Bug Reports** | Verifiable defects with reproduction steps | 3–5 business days |
| **Discussions / Questions** | Usage advice, architecture feedback | Community-driven |

---

## Enterprise Support & Inquiries

For corporate integration support, security architecture reviews, or specialized inquiries, contact:

* **Maintainer**: Erickson Lopez
* **Email**: [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)
* **Website**: [https://ericksonlopez.dev](https://ericksonlopez.dev)
