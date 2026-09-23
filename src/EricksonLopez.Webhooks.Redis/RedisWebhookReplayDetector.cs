// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace EricksonLopez.Webhooks.Redis;

/// <summary>
/// Represents a distributed webhook replay attack detector backed by Redis.
/// </summary>
/// <remarks>
/// Leverages atomic Redis SETNX operations to prevent duplicate message processing across distributed nodes.
/// </remarks>
public sealed class RedisWebhookReplayDetector : IWebhookReplayDetector
{
    private readonly IDatabase _database;
    private readonly TimeSpan _retentionPeriod;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisWebhookReplayDetector"/> class.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer instance.</param>
    /// <param name="retentionPeriod">The duration for which processed message identifiers are retained in Redis.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionMultiplexer"/> is <see langword="null"/></exception>
    public RedisWebhookReplayDetector(IConnectionMultiplexer connectionMultiplexer, TimeSpan retentionPeriod)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        _database = connectionMultiplexer.GetDatabase();
        _retentionPeriod = retentionPeriod;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryRecordAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        var key = $"webhook:replay:{messageId}";

        // SETNX (StringSetAsync with When.NotExists) ensures atomic operations across distributed nodes.
        // If it returns true, the key was set and the event is new.
        // If it returns false, the key already existed and this is a replay.
        return await _database.StringSetAsync(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), _retentionPeriod, When.NotExists).ConfigureAwait(false);
    }
}
