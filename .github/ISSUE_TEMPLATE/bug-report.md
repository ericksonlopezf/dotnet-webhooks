---
name: Bug Report
about: Create a report to help us identify and fix a defect or regression
title: "[BUG] "
labels: ["bug"]
assignees: ericksonlopezf
---

### Affected Component & Version

- **Package**:
  - [ ] `EricksonLopez.Webhooks`
  - [ ] `EricksonLopez.Webhooks.AspNetCore`
  - [ ] `EricksonLopez.Webhooks.Redis`
- **Package Version**: (e.g. 2.0.0)
- **Target Framework**:
  - [ ] .NET 8 (`net8.0`)
  - [ ] .NET 9 (`net9.0`)
  - [ ] .NET 10 (`net10.0`)
- **Hosting Environment / OS**: (e.g. Ubuntu 24.04, Windows Server 2022, Alpine Linux Container)
- **Native AOT**: 
  - [ ] Yes (`PublishAot=true`)
  - [ ] No (JIT runtime)
- **Webhook Protocol**:
  - [ ] Standard (`webhook-*`)
  - [ ] EricksonLopez (`X-Webhook-*`)
- **Dead-Letter Queue / Idempotency Provider**:
  - [ ] In-Memory (`InMemoryWebhookDeadLetterSink` / `InMemoryWebhookReplayDetector`)
  - [ ] Redis (`RedisWebhookDeadLetterSink` / `RedisWebhookReplayDetector`)
  - [ ] Custom implementation

---

### Description of the Bug

A clear and concise description of what the bug is.

### Steps to Reproduce

1. Configure services with `...`
2. Dispatch or receive webhook payload `...`
3. Notice unexpected result `...`

### Minimal Reproduction Code

```csharp
// Paste a minimal, self-contained reproduction snippet here
```

### Expected Behavior

A clear and concise description of what you expected to happen.

### Actual Behavior / Logs / Error Output

```text
// Paste relevant logs, structured telemetry, or error messages here
```

### Additional Context

Add any other context about the problem (e.g. reverse proxy, Cloudflare, load balancer, clock skew issues).
