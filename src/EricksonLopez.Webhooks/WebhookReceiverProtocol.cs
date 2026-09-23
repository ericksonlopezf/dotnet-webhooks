// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Webhooks;

/// <summary>
/// Specifies the HTTP header protocol convention expected when validating inbound webhooks.
/// </summary>
public enum WebhookReceiverProtocol
{
    /// <summary>
    /// Detects the protocol convention automatically based on the presence of inbound HTTP request headers.
    /// </summary>
    AutoDetect = 0,

    /// <summary>
    /// Requires the custom EricksonLopez protocol headers (<c>x-webhook-*</c>).
    /// </summary>
    EricksonLopez = 1,

    /// <summary>
    /// Requires the StandardWebhooks specification headers (<c>webhook-*</c>).
    /// </summary>
    Standard = 2
}
