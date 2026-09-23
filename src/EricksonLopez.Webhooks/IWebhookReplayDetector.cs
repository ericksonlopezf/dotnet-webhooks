// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Defines a contract for detecting and preventing replay attacks by tracking processed webhook message identifiers.
/// </summary>
public interface IWebhookReplayDetector
{
    /// <summary>
    /// Attempts to record a webhook message identifier to detect duplicate transmissions.
    /// </summary>
    /// <param name="messageId">The unique message identifier to record.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains <see langword="true"/>
    /// if the message identifier was successfully recorded; otherwise, <see langword="false"/> if a replay was detected.
    /// </returns>
    ValueTask<bool> TryRecordAsync(string messageId, CancellationToken cancellationToken = default);
}
