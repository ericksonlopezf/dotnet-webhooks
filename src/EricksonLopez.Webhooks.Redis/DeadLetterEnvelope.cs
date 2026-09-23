// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks.Redis;

/// <summary>
/// Represents a dead-lettered webhook transmission envelope stored in persistent storage.
/// </summary>
/// <param name="TargetUrl">The target webhook endpoint URL string.</param>
/// <param name="Payload">The webhook payload envelope containing event metadata and data.</param>
/// <param name="LastResult">The delivery outcome and telemetry from the final attempt.</param>
public sealed record DeadLetterEnvelope(string TargetUrl, WebhookPayload Payload, WebhookDeliveryResult LastResult);
