// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents an in-memory thread-safe bounded dead-letter sink for development and testing environments.
/// </summary>
/// <remarks>
/// This type is intended for testing and development only. For production deployments,
/// use a durable distributed dead-letter queue such as Redis or a persistent broker.
/// </remarks>
public sealed class InMemoryWebhookDeadLetterSink : IWebhookDeadLetterSink
{
    private readonly Queue<(Uri TargetUrl, WebhookPayload Payload, WebhookDeliveryResult LastResult)> _queue;
    private readonly int _maxCapacity;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryWebhookDeadLetterSink"/> class with a default maximum capacity of 1,000 entries.
    /// </summary>
    public InMemoryWebhookDeadLetterSink() : this(1000)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryWebhookDeadLetterSink"/> class with the specified capacity limit.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of dead-letter entries to retain before dropping the oldest.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCapacity"/> is less than or equal to zero</exception>
    public InMemoryWebhookDeadLetterSink(int maxCapacity)
    {
        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCapacity), "Max capacity must be greater than zero.");
        }

        _maxCapacity = maxCapacity;
        _queue = new Queue<(Uri, WebhookPayload, WebhookDeliveryResult)>(maxCapacity);
    }

    /// <summary>
    /// Gets the number of items currently held in the dead-letter sink.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Gets the maximum capacity configured for this dead-letter sink.
    /// </summary>
    public int MaxCapacity => _maxCapacity;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="targetUrl"/> or <paramref name="payload"/> is <see langword="null"/></exception>
    public Task EnqueueAsync(
        Uri targetUrl,
        WebhookPayload payload,
        WebhookDeliveryResult lastResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        ArgumentNullException.ThrowIfNull(payload);

        lock (_syncRoot)
        {
            if (_queue.Count >= _maxCapacity)
            {
                _queue.Dequeue(); // Drop oldest
            }
            _queue.Enqueue((targetUrl, payload, lastResult));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves a snapshot of all dead-lettered entries currently held in the sink.
    /// </summary>
    /// <returns>A read-only collection containing the snapshot of dead-lettered entries.</returns>
    public IReadOnlyCollection<(Uri TargetUrl, WebhookPayload Payload, WebhookDeliveryResult LastResult)> GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _queue.ToArray();
        }
    }
}
