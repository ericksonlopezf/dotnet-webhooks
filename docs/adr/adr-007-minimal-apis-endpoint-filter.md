# ADR-007: ASP.NET Core Minimal APIs Endpoint Filter Integration

## Status
Accepted

## Date
2026-09-05

## Context
ASP.NET Core 7.0+ introduced Endpoint Filters (`IEndpointFilter`) as the standard composable per-route filter mechanism for Minimal APIs. Prior to this, `EricksonLopez.Webhooks.AspNetCore` provided incoming signature verification solely via pipeline middleware (`WebhookReceiverMiddleware` registered via `app.UseWebhookReceiver()`).

While pipeline middleware is effective for global route intercepts, modern cloud-native architectures frequently prefer route-level or route group-level declarations. Requiring developers to configure explicit path strings in `WebhookReceiverOptions.RoutePath` can lead to misconfigurations or friction in modular Minimal API setups.

## Decision
We introduce native Minimal APIs endpoint filter support in `EricksonLopez.Webhooks.AspNetCore`:

1. **`WebhookEndpointFilter`**: An implementation of `IEndpointFilter` that resolves `IWebhookValidator` from `HttpContext.RequestServices` and executes asynchronous cryptographic validation against incoming HTTP requests.
2. **Rejection Responses**: If signature verification fails, the filter returns a typed JSON response matching the existing schema (`WebhookErrorResponse`) with HTTP 401 Unauthorized, or HTTP 413 Payload Too Large if the maximum request size was exceeded.
3. **Structured Logging**: Emits high-performance, zero-allocation structured warning logs using source-generated `[LoggerMessage]`.
4. **Fluent Extensions**:
   - `builder.RequireWebhookSignature()` on `RouteHandlerBuilder` for individual route protection.
   - `group.RequireWebhookSignature()` on `RouteGroupBuilder` for securing entire route subgroups.

## Consequences

### Positive
- Native support for modern ASP.NET Core Minimal APIs patterns.
- Eliminates the need for path-matching string comparisons when configuring endpoints individually.
- Full compatibility with Native AOT via `WebhookJsonContext` for serializing error responses.
- Complete parity with both controller-based and Minimal API-based architectures.

### Negative
- Increases public API surface in `EricksonLopez.Webhooks.AspNetCore`.

## References
- [ADR-001: Webhook Delivery Model](adr-001-webhook-delivery-model.md)
- [ADR-005: Dual Protocol Header Compatibility](adr-005-dual-protocol-header-compatibility.md)
