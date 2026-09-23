// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Webhooks;

/// <summary>
/// Defines standard HTTP header constants used in enterprise webhook transmission and signature verification.
/// </summary>
public static class WebhookHeaders
{
    /// <summary>
    /// Represents the HMAC-SHA256 cryptographic signature header name for the EricksonLopez protocol.
    /// </summary>
    public const string Signature = "X-Webhook-Signature";

    /// <summary>
    /// Represents the UNIX timestamp header name for the EricksonLopez protocol.
    /// </summary>
    public const string Timestamp = "X-Webhook-Timestamp";

    /// <summary>
    /// Represents the domain event type header name for the EricksonLopez protocol.
    /// </summary>
    public const string EventType = "X-Webhook-Event";

    /// <summary>
    /// Represents the unique delivery identifier header name for the EricksonLopez protocol.
    /// </summary>
    public const string DeliveryId = "X-Webhook-Delivery-Id";

    /// <summary>
    /// Represents the HMAC-SHA256 signature header name for the StandardWebhooks specification.
    /// </summary>
    public const string StandardSignature = "webhook-signature";

    /// <summary>
    /// Represents the UNIX timestamp header name for the StandardWebhooks specification.
    /// </summary>
    public const string StandardTimestamp = "webhook-timestamp";

    /// <summary>
    /// Represents the unique message identifier header name for the StandardWebhooks specification.
    /// </summary>
    public const string StandardDeliveryId = "webhook-id";

    /// <summary>
    /// Represents the event type header name for the StandardWebhooks specification.
    /// </summary>
    public const string StandardEventType = "webhook-event";
}
