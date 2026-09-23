// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Webhooks;

/// <summary>
/// Specifies the HTTP header protocol convention applied during webhook transmission and signature verification.
/// </summary>
public enum WebhookProtocol
{
    /// <summary>
    /// Specifies the custom enterprise header format using <c>X-Webhook-*</c> headers.
    /// </summary>
    EricksonLopez = 0,

    /// <summary>
    /// Specifies the StandardWebhooks specification format using <c>webhook-*</c> headers.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Specifies that the protocol should be detected automatically from inbound HTTP request headers.
    /// </summary>
    /// <remarks>
    /// Applies only to inbound receiver verification and cannot be used during outbound dispatch.
    /// </remarks>
    AutoDetect = 2
}
