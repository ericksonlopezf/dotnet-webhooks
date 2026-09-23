// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Webhooks;

namespace EricksonLopez.Webhooks.Sample;

/// <summary>
/// Sample custom audit implementation of <see cref="IWebhookDeadLetterSink"/> for Level 8 showcase.
/// </summary>
public sealed class CustomAuditDeadLetterSink : IWebhookDeadLetterSink
{
    public List<string> LoggedEvents { get; } = new();

    public Task EnqueueAsync(Uri targetUrl, WebhookPayload payload, WebhookDeliveryResult lastResult, CancellationToken cancellationToken = default)
    {
        LoggedEvents.Add($"[{payload.DeliveryId}] {targetUrl} failed with HTTP {lastResult.StatusCode} after {lastResult.Attempts} attempt(s).");
        return Task.CompletedTask;
    }
}
