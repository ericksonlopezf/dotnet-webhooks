// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Webhooks;

/// <summary>
/// Specifies the HTTP header protocol convention used when dispatching outbound webhooks.
/// </summary>
public enum WebhookSenderProtocol
{
    /// <summary>
    /// Specifies the custom EricksonLopez header format (<c>x-webhook-*</c>).
    /// </summary>
    EricksonLopez = 0,

    /// <summary>
    /// Specifies the StandardWebhooks specification header format (<c>webhook-*</c>).
    /// </summary>
    Standard = 1
}
