// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents an error that occurs when a webhook payload exceeds the maximum permitted byte size.
/// </summary>
public sealed class WebhookPayloadTooLargeException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookPayloadTooLargeException"/> class with the specified maximum byte limit.
    /// </summary>
    /// <param name="maxBytes">The maximum allowable byte size that was exceeded.</param>
    public WebhookPayloadTooLargeException(long maxBytes)
        : base($"Webhook payload size exceeds the maximum allowable limit of {maxBytes} bytes.")
    {
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookPayloadTooLargeException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public WebhookPayloadTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookPayloadTooLargeException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or <see langword="null"/> if no inner exception is specified.</param>
    public WebhookPayloadTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the maximum allowable payload size in bytes that was exceeded, if specified.
    /// </summary>
    public long MaxBytes { get; }
}
