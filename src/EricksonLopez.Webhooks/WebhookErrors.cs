// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides factory methods for constructing strongly-typed domain errors for webhook operations.
/// </summary>
public static class WebhookErrors
{
    /// <summary>
    /// Creates an error representing a permanent delivery failure after exhausting all retry attempts.
    /// </summary>
    /// <param name="targetUrl">The destination webhook URL.</param>
    /// <param name="attempts">The total number of delivery attempts executed.</param>
    /// <param name="detail">Additional error details or HTTP status code info.</param>
    /// <returns>An <see cref="Error"/> representing the permanent delivery failure.</returns>
    public static Error DeliveryFailed(Uri targetUrl, int attempts, string? detail) =>
        Error.Failure(
            "WebhookSender.DeliveryFailed",
            $"Webhook delivery to '{targetUrl}' failed after {attempts} attempts. Detail: {detail ?? "Unknown error"}.");

    /// <summary>
    /// Creates an error representing a security policy violation, such as an SSRF attempt.
    /// </summary>
    /// <param name="reason">The explanation of the security violation.</param>
    /// <returns>An <see cref="Error"/> representing the security policy violation.</returns>
    public static Error SecurityViolation(string reason) =>
        Error.Failure(
            "WebhookSender.SecurityViolation",
            $"Webhook delivery blocked by security policy: {reason}");

    /// <summary>
    /// Creates an error representing an overall delivery timeout.
    /// </summary>
    /// <param name="targetUrl">The destination webhook URL.</param>
    /// <param name="timeout">The configured overall delivery timeout that was exceeded.</param>
    /// <returns>An <see cref="Error"/> representing the delivery timeout.</returns>
    public static Error Timeout(Uri targetUrl, TimeSpan timeout) =>
        Error.Failure(
            "WebhookSender.OverallTimeout",
            $"Webhook delivery to '{targetUrl}' exceeded the overall delivery timeout limit of {timeout.TotalSeconds:F1}s.");

    /// <summary>
    /// Creates an error representing an invalid webhook configuration.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <returns>An <see cref="Error"/> representing the configuration validation error.</returns>
    public static Error InvalidConfiguration(string message) =>
        Error.Validation(
            "WebhookSender.InvalidConfiguration",
            message);

    /// <summary>
    /// Creates an error representing a payload size limit violation.
    /// </summary>
    /// <param name="maxSizeBytes">The maximum permitted size in bytes.</param>
    /// <returns>An <see cref="Error"/> representing the payload size limit violation.</returns>
    public static Error PayloadTooLarge(long maxSizeBytes) =>
        Error.Validation(
            "WebhookSender.PayloadTooLarge",
            $"The payload exceeds the maximum permitted size of {maxSizeBytes} bytes.");
}
