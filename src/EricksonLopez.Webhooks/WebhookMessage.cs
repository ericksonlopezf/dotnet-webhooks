// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Encapsulates all necessary parameters and payload data required to deliver an outbound webhook.
/// </summary>
/// <remarks>
/// This type is immutable and thread-safe. Secret material is protected from accidental disclosure in string representations.
/// </remarks>
public sealed record WebhookMessage
{
    /// <summary>
    /// Gets the destination webhook endpoint URL.
    /// </summary>
    public Uri TargetUrl { get; }

    /// <summary>
    /// Gets the shared signing secret key that generates cryptographic signatures.
    /// </summary>
    public WebhookSecret SecretKey { get; }

    /// <summary>
    /// Gets the type name of the domain event.
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// Gets the unique event or idempotency identifier.
    /// </summary>
    public string EventId { get; }

    /// <summary>
    /// Gets the string payload, or <see langword="null"/> if <see cref="PayloadBytes"/> is used.
    /// </summary>
    public string? PayloadString { get; }

    /// <summary>
    /// Gets the raw byte payload, or <see langword="null"/> if <see cref="PayloadString"/> is used.
    /// </summary>
    public ReadOnlyMemory<byte>? PayloadBytes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookMessage"/> class with a string payload.
    /// </summary>
    /// <param name="targetUrl">The destination webhook endpoint URL.</param>
    /// <param name="secretKey">The shared secret key that signs the webhook payload.</param>
    /// <param name="eventType">The type name of the domain event.</param>
    /// <param name="eventId">The explicit event or idempotency identifier.</param>
    /// <param name="payloadString">The serialized JSON or string payload to deliver.</param>
    /// <exception cref="ArgumentNullException"><paramref name="targetUrl"/>, <paramref name="eventType"/>, or <paramref name="payloadString"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="eventId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public WebhookMessage(Uri targetUrl, WebhookSecret secretKey, string eventType, string eventId, string payloadString)
    {
        TargetUrl = targetUrl ?? throw new ArgumentNullException(nameof(targetUrl));
        SecretKey = secretKey;
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        EventId = string.IsNullOrWhiteSpace(eventId) ? throw new ArgumentException("EventId cannot be null or empty. Provide a strict idempotency key.", nameof(eventId)) : eventId;
        PayloadString = payloadString ?? throw new ArgumentNullException(nameof(payloadString));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookMessage"/> class with a byte memory payload.
    /// </summary>
    /// <param name="targetUrl">The destination webhook endpoint URL.</param>
    /// <param name="secretKey">The shared secret key that signs the webhook payload.</param>
    /// <param name="eventType">The type name of the domain event.</param>
    /// <param name="eventId">The explicit event or idempotency identifier.</param>
    /// <param name="payloadBytes">The byte memory containing the serialized payload to deliver.</param>
    /// <exception cref="ArgumentNullException"><paramref name="targetUrl"/> or <paramref name="eventType"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="eventId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public WebhookMessage(Uri targetUrl, WebhookSecret secretKey, string eventType, string eventId, ReadOnlyMemory<byte> payloadBytes)
    {
        TargetUrl = targetUrl ?? throw new ArgumentNullException(nameof(targetUrl));
        SecretKey = secretKey;
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        EventId = string.IsNullOrWhiteSpace(eventId) ? throw new ArgumentException("EventId cannot be null or empty. Provide a strict idempotency key.", nameof(eventId)) : eventId;
        PayloadBytes = payloadBytes;
    }

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"TargetUrl = {TargetUrl}, SecretKey = [REDACTED], EventType = {EventType}, EventId = {EventId}, PayloadString = {PayloadString}, PayloadBytes = {PayloadBytes}");
        return true;
    }
}
