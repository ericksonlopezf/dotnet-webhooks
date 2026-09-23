# ADR-010: Server-Side Request Forgery (SSRF) Defense and DNS Rebinding Mitigation

## Status
Accepted

## Date
2026-09-05

## Context
Outbound webhook dispatch engines require sending HTTP POST requests to arbitrary, user-configured endpoints across the internet. This capability introduces a severe attack vector: Server-Side Request Forgery (SSRF).

Adversaries or compromised tenant accounts could configure destination URLs targeting:
1. Cloud provider instance metadata endpoints (e.g. `http://169.254.169.254/latest/meta-data/` on AWS, GCP, or Azure) to steal IAM credentials or service tokens.
2. Local loopback addresses (`127.0.0.1`, `::1`, `localhost`) to access internal administrative services or Redis caches.
3. Private internal network addresses (RFC 1918 subnets `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`) to map internal topology or exploit unauthenticated internal microservices.

Furthermore, naive application-level URL preflight checks (resolving DNS and checking the IP before calling `HttpClient.SendAsync`) are vulnerable to **DNS Rebinding attacks** (a Time-Of-Check to Time-Of-Use / TOCTOU race condition), where the domain resolves to an authorized public IP during validation, but returns a forbidden private IP when `HttpClient` initiates the actual TCP connection.

## Decision
We integrate zero-trust network perimeter defense directly into `EricksonLopez.Webhooks` via `EricksonLopez.Security.Network` and `SafeSocketsHttpHandlerFactory` using **two independent, defense-in-depth layers**:

1. **Layer 1 — DNS Preflight (`SafeDnsResolver` in `WebhookSender`):**
   - Before `HttpClient.SendAsync` is called, `WebhookSender.SendAsync` performs an explicit DNS resolution via `SafeDnsResolver` (from `EricksonLopez.Security.Network`).
   - This preflight checks whether any resolved IP address is a forbidden range (loopback, private, link-local, cloud metadata). If blocked, a `WebhookErrors.SecurityViolation` is returned immediately without opening a connection.
   - While effective for most cases, this layer alone is vulnerable to DNS rebinding TOCTOU attacks.

2. **Layer 2 — Connection-Time Socket Validation (`ConnectCallback` in `SafeSocketsHttpHandlerFactory`):**
   - `SafeSocketsHttpHandlerFactory` constructs a hardened `SocketsHttpHandler` with a custom `ConnectCallback` lambda.
   - This callback resolves DNS and validates the final resolved IP **at the exact moment the TCP socket is established** — after the `HttpClient` has decided to connect — completely neutralizing DNS rebinding TOCTOU attacks where a domain resolves to an allowed IP during Layer 1 but switches to a forbidden IP by the time of actual connection.
   - Both layers must allow a destination for the connection to succeed.

3. **Comprehensive Range Blocking (both layers):**
   - IPv4 and IPv6 loopback, link-local, cloud metadata (169.254.169.254), broadcast, multicast, and RFC 1918 private subnets are blocked at both layers.

4. **URL Scheme Preflight Validation:**
   - `WebhookSender.SendAsync` validates that destination URLs strictly use `http` or `https` schemes, fast-failing with `WebhookErrors.InvalidConfiguration` on unsupported or malicious schemes (e.g. `file://`, `gopher://`, `ftp://`).

5. **Configuration Default:**
   - SSRF protection is enabled by default in `WebhookSenderOptions` (`EnableSsrfProtection = true`).
   - Senders created via `WebhookSender.CreateSafeSender(options)` or via DI extension `AddWebhooks(options)` automatically configure the secure network perimeter.

## Consequences

### Positive
- Robust, out-of-the-box protection against critical SSRF and cloud credential theft vulnerabilities.
- Complete mitigation of DNS rebinding race conditions at the socket layer.
- Zero external runtime SaaS dependencies; relies on battle-tested ecosystem security components.
- Compliant with enterprise DevSecOps and SOC 2 / ISO 27001 network perimeter standards.

### Negative
- Local integration tests wishing to dispatch to `http://localhost` must explicitly provide a test `HttpMessageHandler` (such as `MockHttpMessageHandler`) or disable SSRF protection for development harnesses.

## References
- [ADR-001: Webhook Delivery Model](adr-001-webhook-delivery-model.md)
- [ADR-002: Package Existence & Invariant Justification](adr-002-package-existence-justification.md)
- OWASP Server-Side Request Forgery Prevention Cheat Sheet
