// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides extension methods for <see cref="IWebhookSender"/> supporting Native AOT-compatible serialization.
/// </summary>
public static class WebhookSenderExtensions
{

    /// <summary>
    /// Serializes the specified payload using ahead-of-time (AOT) type metadata and delivers the webhook.
    /// </summary>
    /// <typeparam name="T">The type of the data payload to serialize.</typeparam>
    /// <param name="sender">The webhook sender instance.</param>
    /// <param name="targetUrl">The destination endpoint URI.</param>
    /// <param name="secretKey">The shared secret key that computes the HMAC signature.</param>
    /// <param name="eventType">The logical domain event type identifier.</param>
    /// <param name="eventId">The explicit event or idempotency identifier.</param>
    /// <param name="data">The typed data object to serialize as JSON.</param>
    /// <param name="jsonTypeInfo">The JSON type information enabling reflection-free Native AOT serialization.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a result indicating
    /// whether delivery succeeded, along with the delivery metadata.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/></exception>
    public static Task<Result<WebhookDeliveryResult>> SendAsync<T>(
        this IWebhookSender sender,
        Uri targetUrl,
        string secretKey,
        string eventType,
        string eventId,
        T data,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var payload = JsonSerializer.SerializeToUtf8Bytes(data, jsonTypeInfo);
        var message = new WebhookMessage(targetUrl, secretKey, eventType, eventId, payload);
        return sender.SendAsync(message, cancellationToken);
    }
}
