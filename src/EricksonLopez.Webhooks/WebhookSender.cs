// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using EricksonLopez.Security.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Dispatches cryptographically signed outbound HTTP webhooks with SSRF mitigation, distributed tracing, and dead-letter queue escalation.
/// </summary>
/// <remarks>
/// This type is thread-safe. Transient failure resilience and retries should be configured on the underlying
/// <see cref="HttpClient"/> using standard HTTP resilience pipelines.
/// </remarks>
public sealed partial class WebhookSender : IWebhookSender
{
    private readonly HttpClient _httpClient;
    private readonly IWebhookDeadLetterSink? _deadLetterQueue;
    private readonly WebhookSenderOptions _options;
    private readonly ILogger<WebhookSender> _logger;
    private readonly SafeDnsResolver? _dnsResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSender"/> class with options and structured logger.
    /// </summary>
    /// <param name="httpClient">The HTTP client that dispatches requests.</param>
    /// <param name="deadLetterQueue">The optional dead-letter sink for unrecoverable delivery failures.</param>
    /// <param name="options">The configuration options for webhook dispatching.</param>
    /// <param name="logger">The logger for structured diagnostic logging.</param>
    internal WebhookSender(
        HttpClient httpClient,
        IWebhookDeadLetterSink? deadLetterQueue = null,
        WebhookSenderOptions? options = null,
        ILogger<WebhookSender>? logger = null)
        : this(httpClient, Options.Create(options ?? new WebhookSenderOptions()), deadLetterQueue, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSender"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client that dispatches requests.</param>
    /// <param name="deadLetterQueue">The optional dead-letter sink for unrecoverable delivery failures.</param>
    /// <param name="options">The configuration options for webhook dispatching.</param>
    internal WebhookSender(
        HttpClient httpClient,
        IWebhookDeadLetterSink? deadLetterQueue,
        WebhookSenderOptions? options)
        : this(httpClient, deadLetterQueue, options, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSender"/> class with Microsoft Options and structured logging.
    /// </summary>
    /// <param name="httpClient">The HTTP client that dispatches requests.</param>
    /// <param name="optionsAccessor">The accessor for sender configuration options.</param>
    /// <param name="deadLetterQueue">The optional dead-letter sink for unrecoverable delivery failures.</param>
    /// <param name="logger">The logger for structured diagnostic logging.</param>
    [ActivatorUtilitiesConstructor]
    internal WebhookSender(
        HttpClient httpClient,
        IOptions<WebhookSenderOptions> optionsAccessor,
        IWebhookDeadLetterSink? deadLetterQueue = null,
        ILogger<WebhookSender>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        _options = optionsAccessor.Value ?? new WebhookSenderOptions();
        _deadLetterQueue = deadLetterQueue;
        _logger = logger ?? NullLogger<WebhookSender>.Instance;

        if (_options.EnableSsrfProtection)
        {
            _dnsResolver = new SafeDnsResolver(_options.SsrfProtection);
            LogManualHttpClientUsed(_logger);
        }
    }

    /// <summary>
    /// Creates a new <see cref="WebhookSender"/> configured with <see cref="SafeSocketsHttpHandlerFactory"/> to prevent SSRF and DNS rebinding attacks.
    /// </summary>
    /// <param name="deadLetterQueue">An optional sink for persisting deliveries that permanently fail.</param>
    /// <param name="options">Configuration options controlling timeout, payload size limits, and security constraints.</param>
    /// <param name="logger">An optional logger instance for diagnostic telemetry.</param>
    /// <returns>A configured <see cref="WebhookSender"/> instance with built-in SSRF protection.</returns>
    public static WebhookSender CreateSafeSender(
        IWebhookDeadLetterSink? deadLetterQueue = null,
        WebhookSenderOptions? options = null,
        ILogger<WebhookSender>? logger = null)
    {
        var senderOptions = options ?? new WebhookSenderOptions();
        var handler = SafeSocketsHttpHandlerFactory.Create(senderOptions);
        var httpClient = new HttpClient(handler);
        return new WebhookSender(httpClient, deadLetterQueue, senderOptions, logger);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/></exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="cancellationToken"/></exception>
    public async Task<Result<WebhookDeliveryResult>> SendAsync(
        WebhookMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var urlValidationError = ValidateTargetUrl(message.TargetUrl, _options);
        if (urlValidationError is not null)
            return urlValidationError;

        var metadataValidationError = ValidateEventMetadata(message.EventType, message.EventId);
        if (metadataValidationError is not null)
            return metadataValidationError;

        var sanitizedTargetUrl = SanitizeTargetUrl(message.TargetUrl);

        var ssrfError = await PerformSsrfPreflightAsync(_dnsResolver, message.TargetUrl, sanitizedTargetUrl, _logger, cancellationToken).ConfigureAwait(false);
        if (ssrfError is not null)
            return ssrfError;

        var timestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ReadOnlyMemory<byte> payloadBytes;
        if (message.PayloadBytes.HasValue)
        {
            payloadBytes = message.PayloadBytes.Value;
        }
        else
        {
            var byteCount = Encoding.UTF8.GetByteCount(message.PayloadString!);
            if (byteCount > _options.MaxPayloadSizeBytes)
            {
                return Result<WebhookDeliveryResult>.Failure(WebhookErrors.PayloadTooLarge(_options.MaxPayloadSizeBytes));
            }
            payloadBytes = Encoding.UTF8.GetBytes(message.PayloadString!);
        }

        if (payloadBytes.Length > _options.MaxPayloadSizeBytes)
        {
            return Result<WebhookDeliveryResult>.Failure(WebhookErrors.PayloadTooLarge(_options.MaxPayloadSizeBytes));
        }

        var signature = WebhookSigner.ComputeSignature(message.SecretKey, timestampSeconds, payloadBytes.Span, _options.Protocol, message.EventId);

        using var activity = WebhookDiagnostics.ActivitySource.StartActivity("webhook.send", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("webhook.delivery_id", message.EventId); // Using EventId as strictly mapped
            activity.SetTag("webhook.event_type", message.EventType);
            activity.SetTag("webhook.target_url", sanitizedTargetUrl.ToString());
            activity.SetTag("webhook.event_id", message.EventId);
        }

        LogDispatchingWebhook(_logger, message.EventId, message.EventType, sanitizedTargetUrl);

        var startTimestamp = Stopwatch.GetTimestamp();
        int lastStatusCode = 0;
        string? lastError = null;
        string? lastResponseSnippet = null;

        using var request = CreateHttpRequest(
            message.TargetUrl,
            payloadBytes,
            signature,
            timestampSeconds,
            message.EventType,
            message.EventId,
            _options.Protocol);

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(_options.Timeout);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                attemptCts.Token).ConfigureAwait(false);

            var duration = Stopwatch.GetElapsedTime(startTimestamp);
            lastStatusCode = (int)response.StatusCode;

            WebhookDiagnostics.DeliveriesTotal.Add(1,
                new KeyValuePair<string, object?>("event_type", message.EventType),
                new KeyValuePair<string, object?>("is_success", response.IsSuccessStatusCode));
            WebhookDiagnostics.DeliveryDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("event_type", message.EventType));

            if (response.IsSuccessStatusCode)
            {
                LogDeliverySuccess(_logger, message.EventId, message.EventType, sanitizedTargetUrl, lastStatusCode, duration.TotalMilliseconds);

                if (activity is not null)
                {
                    activity.SetTag("http.status_code", lastStatusCode);
                    activity.SetTag("webhook.attempts", 1);
                    activity.SetTag("webhook.is_success", true);
                }

                return Result<WebhookDeliveryResult>.Success(new WebhookDeliveryResult(
                    IsSuccess: true,
                    StatusCode: lastStatusCode,
                    Attempts: 1,
                    Duration: duration));
            }

            lastError = $"HTTP {lastStatusCode}: {response.ReasonPhrase}";
            lastResponseSnippet = await CaptureResponseSnippetAsync(response, attemptCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            lastError = $"Delivery timeout of {_options.Timeout.TotalSeconds:F1}s exceeded.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            lastError = ex.Message;
            WebhookDiagnostics.DeliveriesTotal.Add(1,
                new KeyValuePair<string, object?>("event_type", message.EventType),
                new KeyValuePair<string, object?>("is_success", false));
        }

        var totalDuration = Stopwatch.GetElapsedTime(startTimestamp);

        LogDeliveryExhausted(_logger, message.EventId, message.EventType, sanitizedTargetUrl, totalDuration.TotalMilliseconds, lastStatusCode, lastError, _deadLetterQueue is not null);

        if (activity is not null)
        {
            activity.SetTag("http.status_code", lastStatusCode);
            activity.SetTag("webhook.attempts", 1);
            activity.SetTag("webhook.is_success", false);
            if (lastError is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, lastError);
            }
        }

        var finalResult = new WebhookDeliveryResult(
            IsSuccess: false,
            StatusCode: lastStatusCode,
            Attempts: 1,
            Duration: totalDuration,
            ErrorMessage: lastError,
            ResponseSnippet: lastResponseSnippet);

        if (_deadLetterQueue is not null)
        {
            WebhookDiagnostics.DeadLetterEscalationsTotal.Add(1,
                new KeyValuePair<string, object?>("event_type", message.EventType));

            var payloadStr = message.PayloadString ?? Encoding.UTF8.GetString(payloadBytes.Span);
            var envelope = new WebhookPayload(
                DeliveryId: message.EventId,
                EventType: message.EventType,
                Payload: payloadStr,
                Timestamp: DateTimeOffset.FromUnixTimeSeconds(timestampSeconds),
                AttemptNumber: 1);

            await _deadLetterQueue.EnqueueAsync(message.TargetUrl, envelope, finalResult, cancellationToken).ConfigureAwait(false);
        }

        return WebhookErrors.DeliveryFailed(sanitizedTargetUrl, 1, lastError);
    }

    private static Error? ValidateTargetUrl(Uri targetUrl, WebhookSenderOptions options)
    {
        if (!targetUrl.IsAbsoluteUri)
        {
            return WebhookErrors.InvalidConfiguration("Target URL must be an absolute URI.");
        }

        if (targetUrl.Scheme != Uri.UriSchemeHttp && targetUrl.Scheme != Uri.UriSchemeHttps)
        {
            return WebhookErrors.InvalidConfiguration("Target URL scheme must be http or https.");
        }

        if (!options.DangerousAllowInsecureHttp && targetUrl.Scheme == Uri.UriSchemeHttp)
        {
            return WebhookErrors.SecurityViolation("Insecure HTTP delivery is not permitted. Enable DangerousAllowInsecureHttp to override.");
        }

        return null;
    }

    private static Error? ValidateEventMetadata(string eventType, string eventId)
    {
        if (eventType.Contains('\r') || eventType.Contains('\n') || eventId.Contains('\r') || eventId.Contains('\n'))
        {
            return WebhookErrors.InvalidConfiguration("Event metadata cannot contain carriage return or newline characters.");
        }

        return null;
    }

    private static Uri SanitizeTargetUrl(Uri targetUrl) =>
        (!string.IsNullOrEmpty(targetUrl.Query) || !string.IsNullOrEmpty(targetUrl.Fragment))
            ? new Uri(targetUrl.GetLeftPart(UriPartial.Path))
            : targetUrl;

    private static async Task<Error?> PerformSsrfPreflightAsync(
        SafeDnsResolver? dnsResolver,
        Uri targetUrl,
        Uri sanitizedTargetUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (dnsResolver is null)
        {
            return null;
        }

        var resolutionResult = await dnsResolver.ResolveAndValidateAsync(targetUrl.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        if (resolutionResult.IsFailure)
        {
            LogSsrfBlocked(logger, sanitizedTargetUrl, resolutionResult.Error.Description);
            return WebhookErrors.SecurityViolation(resolutionResult.Error.Description);
        }

        return null;
    }

    private static HttpRequestMessage CreateHttpRequest(
        Uri targetUrl,
        ReadOnlyMemory<byte> payloadBytes,
        string signature,
        long timestampSeconds,
        string eventType,
        string webhookMsgId,
        WebhookSenderProtocol protocol)
    {
        var content = new ReadOnlyMemoryContent(payloadBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = content
        };

        var sigHeader = protocol == WebhookSenderProtocol.Standard ? WebhookHeaders.StandardSignature : WebhookHeaders.Signature;
        var timeHeader = protocol == WebhookSenderProtocol.Standard ? WebhookHeaders.StandardTimestamp : WebhookHeaders.Timestamp;
        var eventHeader = protocol == WebhookSenderProtocol.Standard ? WebhookHeaders.StandardEventType : WebhookHeaders.EventType;
        var idHeader = protocol == WebhookSenderProtocol.Standard ? WebhookHeaders.StandardDeliveryId : WebhookHeaders.DeliveryId;

        request.Headers.Add(sigHeader, signature);
        request.Headers.Add(timeHeader, timestampSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(eventHeader, eventType);
        request.Headers.Add(idHeader, webhookMsgId);

        if (Activity.Current is not null)
        {
            request.Headers.TryAddWithoutValidation("traceparent", Activity.Current.Id);
            if (!string.IsNullOrEmpty(Activity.Current.TraceStateString))
            {
                request.Headers.TryAddWithoutValidation("tracestate", Activity.Current.TraceStateString);
            }
        }

        return request;
    }

    private static async Task<string?> CaptureResponseSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[512];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            return bytesRead > 0 ? Encoding.UTF8.GetString(buffer, 0, bytesRead) : null;
        }
        catch
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Dispatching webhook {DeliveryId} for event {EventType} to {TargetUrl}")]
    private static partial void LogDispatchingWebhook(ILogger logger, string deliveryId, string eventType, Uri targetUrl);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Successfully delivered webhook {DeliveryId} for event {EventType} to {TargetUrl} with HTTP {StatusCode} in {DurationMs:F1}ms")]
    private static partial void LogDeliverySuccess(ILogger logger, string deliveryId, string eventType, Uri targetUrl, int statusCode, double durationMs);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Webhook {DeliveryId} for event {EventType} to {TargetUrl} failed (Duration: {DurationMs:F1}ms). Status: {StatusCode}, Error: {Error}. DLQ Escalation: {HasDlq}")]
    private static partial void LogDeliveryExhausted(ILogger logger, string deliveryId, string eventType, Uri targetUrl, double durationMs, int statusCode, string? error, bool hasDlq);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Webhook delivery to '{TargetUrl}' was blocked by SSRF policy: {Error}")]
    private static partial void LogSsrfBlocked(ILogger logger, Uri targetUrl, string error);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "WebhookSender initialized with custom HttpClient. Ensure SafeSocketsHttpHandler is configured to protect against DNS rebinding SSRF.")]
    private static partial void LogManualHttpClientUsed(ILogger logger);
}
