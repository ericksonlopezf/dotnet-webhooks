# ADR-005: Dual-Protocol Header Compatibility (StandardWebhooks and EricksonLopez)

## Status
Accepted

## Date
2026-09-04

## Context
Two competing header naming conventions exist in the webhook ecosystem:
1. **EricksonLopez Protocol**: Traditional RFC-compliant enterprise prefix headers (`X-Webhook-Signature`, `X-Webhook-Timestamp`, `X-Webhook-Event`, `X-Webhook-Delivery-Id`).
2. **StandardWebhooks Specification**: Emerging industry standard adopted by Svix, OpenAI, GitHub, and Twilio (`webhook-signature`, `webhook-timestamp`, `webhook-id`, `webhook-event`).

Enforcing only one convention restricts either internal enterprise consistency or third-party SaaS interoperability.

## Decision
We implement a dual-protocol compatibility architecture governed by dedicated sender and receiver enumerations:

1. **Protocol Enumerations**:
   - `WebhookSenderProtocol`: Controls outbound dispatch formatting (`EricksonLopez` = 0, `Standard` = 1).
   - `WebhookReceiverProtocol`: Controls inbound ingress header extraction (`AutoDetect` = 0, `EricksonLopez` = 1, `Standard` = 2).
2. **Defaults**:
   - Outbound dispatching defaults to `WebhookSenderProtocol.EricksonLopez` to ensure backwards compatibility for existing enterprise subscribers.
   - Inbound receiving defaults to `WebhookReceiverProtocol.AutoDetect`, allowing receivers to process both enterprise callbacks and StandardWebhooks payloads seamlessly.
3. **Cryptographic Signing Formats**:
   - `EricksonLopez`: Emits `X-Webhook-Signature` (`v1=<hex>`) computed over `${timestampSeconds}.${payload}`.
   - `StandardWebhooks`: Emits `webhook-signature` (`v1,<base64>`) computed over the canonical specification payload `${webhookId}.${timestamp}.${payload}`. Both protocols enforce constant-time equality validation (`FixedTimeEquals`).

## Consequences

### Positive
- Allows seamless interoperability with third-party webhooks (Stripe, OpenAI, Svix, GitHub).
- Preserves full backwards compatibility with existing enterprise deployments.
- No performance degradation: header resolution evaluates static string constants without regex or dynamic string concatenation.

### Negative
- Slightly expanded header constant space in `WebhookHeaders`.

## References
- [ADR-001: Webhook Delivery Model](./adr-001-webhook-delivery-model.md)
- [Product Strategy](../product-strategy.md) (Initiative 9)

