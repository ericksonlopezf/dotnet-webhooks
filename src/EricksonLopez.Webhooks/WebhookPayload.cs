// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents the outbound envelope containing metadata and content delivered to a webhook subscriber.
/// </summary>
/// <param name="DeliveryId">The unique identifier for this delivery attempt.</param>
/// <param name="EventType">The type name of the emitted domain event.</param>
/// <param name="Payload">The JSON or structured payload string content.</param>
/// <param name="Timestamp">The timestamp at which the webhook was generated.</param>
/// <param name="AttemptNumber">The transmission attempt count for this delivery.</param>
public sealed record WebhookPayload(
    string DeliveryId,
    string EventType,
    string Payload,
    DateTimeOffset Timestamp,
    int AttemptNumber = 1);
