// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace EricksonLopez.Webhooks.Redis;

/// <summary>
/// Represents a distributed dead-letter queue sink backed by a Redis list.
/// </summary>
/// <remarks>
/// Failed delivery attempts are serialized to JSON and pushed to the specified Redis list key.
/// </remarks>
public sealed class RedisWebhookDeadLetterSink : IWebhookDeadLetterSink
{
    private readonly IDatabase _database;
    private readonly string _listKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisWebhookDeadLetterSink"/> class.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer instance.</param>
    /// <param name="listKey">The Redis list key name where failed payloads will be appended.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionMultiplexer"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="listKey"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public RedisWebhookDeadLetterSink(IConnectionMultiplexer connectionMultiplexer, string listKey = "webhook:dlq")
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        if (string.IsNullOrWhiteSpace(listKey))
        {
            throw new ArgumentException("List key cannot be null or whitespace.", nameof(listKey));
        }

        _database = connectionMultiplexer.GetDatabase();
        _listKey = listKey;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="targetUrl"/> or <paramref name="payload"/> is <see langword="null"/></exception>
    public async Task EnqueueAsync(Uri targetUrl, WebhookPayload payload, WebhookDeliveryResult lastResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        ArgumentNullException.ThrowIfNull(payload);

        var envelope = new DeadLetterEnvelope(targetUrl.ToString(), payload, lastResult);
        var serializedPayload = JsonSerializer.Serialize(envelope, DeadLetterJsonContext.Default.DeadLetterEnvelope);

        // Push to the tail of the list
        await _database.ListRightPushAsync(_listKey, serializedPayload).ConfigureAwait(false);
    }
}
