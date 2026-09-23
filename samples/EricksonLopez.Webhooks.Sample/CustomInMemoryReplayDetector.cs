// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Webhooks;

namespace EricksonLopez.Webhooks.Sample;

/// <summary>
/// Sample custom in-memory implementation of <see cref="IWebhookReplayDetector"/> for Level 8 showcase.
/// </summary>
public sealed class CustomInMemoryReplayDetector : IWebhookReplayDetector
{
    private readonly HashSet<string> _seen = new();

    public ValueTask<bool> TryRecordAsync(string messageId, CancellationToken cancellationToken = default)
    {
        lock (_seen)
        {
            return ValueTask.FromResult(_seen.Add(messageId));
        }
    }
}
