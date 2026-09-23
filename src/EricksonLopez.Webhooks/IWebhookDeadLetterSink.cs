// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Defines a storage contract for failed, unrecoverable webhook deliveries that have exceeded retry limits.
/// </summary>
public interface IWebhookDeadLetterSink
{
    /// <summary>
    /// Enqueues a failed webhook delivery into the dead-letter sink.
    /// </summary>
    /// <param name="targetUrl">The target webhook endpoint URL.</param>
    /// <param name="payload">The delivery payload envelope containing event metadata and data.</param>
    /// <param name="lastResult">The outcome of the final delivery attempt.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        Uri targetUrl,
        WebhookPayload payload,
        WebhookDeliveryResult lastResult,
        CancellationToken cancellationToken = default);
}
