// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents a dead-letter sink that silently discards failed messages without persistence.
/// </summary>
/// <remarks>
/// Serves as a safe default implementation to avoid unbounded memory consumption when no persistent sink is registered.
/// </remarks>
public sealed class NoOpWebhookDeadLetterSink : IWebhookDeadLetterSink
{
    /// <inheritdoc/>
    public Task EnqueueAsync(Uri targetUrl, WebhookPayload payload, WebhookDeliveryResult lastResult, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
