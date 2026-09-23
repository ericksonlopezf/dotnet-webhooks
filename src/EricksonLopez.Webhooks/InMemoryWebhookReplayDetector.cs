// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents an in-memory replay attack detector for single-node webhook processing.
/// </summary>
/// <remarks>
/// This type is thread-safe. It tracks processed message identifiers in memory and
/// is not suitable for distributed environments or multi-node clusters where a shared store is required.
/// </remarks>
public sealed class InMemoryWebhookReplayDetector : IWebhookReplayDetector, IDisposable
{
    private readonly ConcurrentDictionary<string, long> _seenMessageIds = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly TimeSpan _retentionPeriod;
    private readonly Timer _cleanupTimer;
    private const int MaxCapacity = 100_000;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryWebhookReplayDetector"/> class with the specified retention period.
    /// </summary>
    /// <param name="retentionPeriod">The duration to retain message identifiers. Typically matches or exceeds the timestamp tolerance window.</param>
    public InMemoryWebhookReplayDetector(TimeSpan retentionPeriod)
    {
        _retentionPeriod = retentionPeriod;

        // Run cleanup every half of the retention period, or at least every 1 minute
        var cleanupInterval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(1).Ticks, retentionPeriod.Ticks / 2));
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, cleanupInterval, cleanupInterval);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryWebhookReplayDetector"/> class with a default retention period of five minutes.
    /// </summary>
    public InMemoryWebhookReplayDetector() : this(TimeSpan.FromMinutes(5))
    {
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The current instance has already been disposed</exception>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public ValueTask<bool> TryRecordAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        if (_seenMessageIds.Count >= MaxCapacity)
        {
            if (_insertionOrder.TryDequeue(out var oldestId))
            {
                _seenMessageIds.TryRemove(oldestId, out _);
            }
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(_retentionPeriod).ToUnixTimeMilliseconds();

        var added = _seenMessageIds.TryAdd(messageId, expiresAt);
        if (added)
        {
            _insertionOrder.Enqueue(messageId);
        }

        return ValueTask.FromResult(added);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kvp in _seenMessageIds)
        {
            if (now > kvp.Value)
            {
                _seenMessageIds.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Releases the unmanaged and managed resources used by the <see cref="InMemoryWebhookReplayDetector"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
        _seenMessageIds.Clear();
    }
}
