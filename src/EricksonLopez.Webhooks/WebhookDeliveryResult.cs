// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Represents the outcome and diagnostic telemetry of a webhook transmission attempt.
/// </summary>
/// <param name="IsSuccess">A value indicating whether the HTTP response was in the successful 2xx status code range.</param>
/// <param name="StatusCode">The HTTP status code returned by the destination endpoint.</param>
/// <param name="Attempts">The total number of delivery attempts executed.</param>
/// <param name="Duration">The elapsed time for the delivery attempt or cumulative duration across all retry attempts.</param>
/// <param name="ErrorMessage">Diagnostic error details if the delivery attempt failed.</param>
/// <param name="ResponseSnippet">A truncated excerpt of the remote HTTP response body when delivery fails.</param>
public readonly record struct WebhookDeliveryResult(
    bool IsSuccess,
    int StatusCode,
    int Attempts,
    TimeSpan Duration,
    string? ErrorMessage = null,
    string? ResponseSnippet = null);

