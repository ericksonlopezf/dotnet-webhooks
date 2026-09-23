// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Defines outbound webhook delivery capabilities with cryptographic signing and automated retry management.
/// </summary>
public interface IWebhookSender
{
    /// <summary>
    /// Delivers an outbound webhook message to the destination endpoint with cryptographic signing.
    /// </summary>
    /// <param name="message">The webhook message envelope containing destination, payload, and cryptographic key.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a result indicating
    /// whether delivery succeeded, along with the delivery metadata.
    /// </returns>
    Task<Result<WebhookDeliveryResult>> SendAsync(
        WebhookMessage message,
        CancellationToken cancellationToken = default);
}
