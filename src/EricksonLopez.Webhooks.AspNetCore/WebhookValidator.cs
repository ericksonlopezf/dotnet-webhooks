// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Enforces HMAC-SHA256 signature verification, replay protection, and timestamp validation for inbound webhook requests.
/// </summary>
/// <remarks>
/// This type is thread-safe. It validates inbound headers, calculates streaming hashes to avoid
/// large object heap allocations, and checks replay detectors when configured.
/// </remarks>
public sealed partial class WebhookValidator : IWebhookValidator
{
    private readonly WebhookReceiverOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IWebhookReplayDetector? _replayDetector;
    private readonly ILogger<WebhookValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookValidator"/> class.
    /// </summary>
    /// <param name="options">The accessor for receiver configuration options.</param>
    /// <param name="timeProvider">An optional time provider that calculates timestamp drift.</param>
    /// <param name="replayDetector">An optional replay detector that prevents duplicate message processing.</param>
    /// <param name="logger">An optional structured logger for diagnostic logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/></exception>
    public WebhookValidator(
        IOptions<WebhookReceiverOptions> options,
        TimeProvider? timeProvider = null,
        IWebhookReplayDetector? replayDetector = null,
        ILogger<WebhookValidator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _replayDetector = replayDetector;
        _logger = logger ?? NullLogger<WebhookValidator>.Instance;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> is <see langword="null"/></exception>
    public async Task<Result<bool>> ValidateRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var activeKeys = new List<string>(_options.GetActiveSecretKeys());
        if (activeKeys.Count == 0)
        {
            RecordInboundValidation(false, "UnconfiguredSecret");
            LogValidationFailed(_logger, "Webhook secret key is not configured on the server.");
            return Error.Failure("WebhookValidator.UnconfiguredSecret", "Webhook secret key is not configured on the server.");
        }

        var request = httpContext.Request;
        var headers = request.Headers;

        // 1. Extract Signature
        string? signature = null;
        if (_options.Protocol is WebhookReceiverProtocol.EricksonLopez or WebhookReceiverProtocol.AutoDetect)
        {
            if (headers.TryGetValue(WebhookHeaders.Signature, out var ericksonSigs) && ericksonSigs.Count > 0)
            {
                signature = GetFirstNonEmptyValue(ericksonSigs);
            }
        }

        if (string.IsNullOrWhiteSpace(signature) && _options.Protocol is WebhookReceiverProtocol.Standard or WebhookReceiverProtocol.AutoDetect)
        {
            if (headers.TryGetValue(WebhookHeaders.StandardSignature, out var stdSigs) && stdSigs.Count > 0)
            {
                signature = GetFirstNonEmptyValue(stdSigs);
            }
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            RecordInboundValidation(false, "MissingSignature");
            LogValidationFailed(_logger, "Request is missing the required signature header.");
            return Error.Unauthorized("WebhookValidator.MissingSignature", "Request is missing the required signature header.");
        }

        // 2. Extract Timestamp
        var hasValidTimestamp = false;
        long timestampSeconds = 0;

        if (_options.Protocol is WebhookReceiverProtocol.EricksonLopez or WebhookReceiverProtocol.AutoDetect)
        {
            if (headers.TryGetValue(WebhookHeaders.Timestamp, out var ericksonTimes) && ericksonTimes.Count > 0)
            {
                var tsStr = GetFirstNonEmptyValue(ericksonTimes);
                hasValidTimestamp = TryParseTimestamp(tsStr, out timestampSeconds);
            }
        }

        if (!hasValidTimestamp && _options.Protocol is WebhookReceiverProtocol.Standard or WebhookReceiverProtocol.AutoDetect)
        {
            if (request.Headers.TryGetValue(WebhookHeaders.StandardTimestamp, out var stdTimes) && stdTimes.Count > 0)
            {
                var tsStr = GetFirstNonEmptyValue(stdTimes);
                hasValidTimestamp = TryParseTimestamp(tsStr, out timestampSeconds);
            }
        }

        if (!hasValidTimestamp)
        {
            RecordInboundValidation(false, "InvalidTimestamp");
            LogValidationFailed(_logger, "Request is missing or contains an invalid timestamp header.");
            return Error.Unauthorized("WebhookValidator.InvalidTimestamp", "Request is missing or contains an invalid timestamp header.");
        }

        // 3. Verify anti-replay timestamp drift tolerance comparing whole seconds
        var currentSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var skewSeconds = unchecked(currentSeconds - timestampSeconds);
        if (skewSeconds < 0)
        {
            skewSeconds = -skewSeconds;
            if (skewSeconds < 0)
            {
                skewSeconds = long.MaxValue;
            }
        }

        if (skewSeconds > (long)_options.TimestampTolerance.TotalSeconds)
        {
            RecordInboundValidation(false, "TimestampExpired");
            var errorMsg = $"Webhook timestamp differs from server time by {skewSeconds}s, exceeding maximum tolerance of {_options.TimestampTolerance.TotalSeconds:F0}s.";
            LogValidationFailed(_logger, errorMsg);
            return Error.Unauthorized("WebhookValidator.TimestampExpired", errorMsg);
        }

        // 4. Extract message/delivery ID if available for StandardWebhooks
        string? webhookId = null;
        if (request.Headers.TryGetValue(WebhookHeaders.StandardDeliveryId, out var idVal) && idVal.Count > 0)
        {
            webhookId = GetFirstNonEmptyValue(idVal);
        }
        else if (request.Headers.TryGetValue(WebhookHeaders.DeliveryId, out var deliveryIdVal) && deliveryIdVal.Count > 0)
        {
            webhookId = GetFirstNonEmptyValue(deliveryIdVal);
        }

        // 5. Verify payload length against MaxPayloadSizeBytes to prevent denial of service (OOM)
        if (request.ContentLength.HasValue && request.ContentLength.Value > _options.MaxPayloadSizeBytes)
        {
            RecordInboundValidation(false, "PayloadTooLarge");
            var errorMsg = $"Webhook payload size of {request.ContentLength.Value} bytes exceeds the maximum allowable limit of {_options.MaxPayloadSizeBytes} bytes.";
            LogValidationFailed(_logger, errorMsg);
            return Error.Validation("WebhookValidator.PayloadTooLarge", errorMsg);
        }

        // Enable stream buffering to allow multiple reads downstream
        request.EnableBuffering();
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        // 6. Verify cryptographic HMAC signature against active secret keys using zero-LOH streaming
        var isValid = false;
        try
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            isValid = await WebhookSigner.VerifySignatureAsync(
                activeKeys,
                timestampSeconds,
                request.Body,
                signature,
                _options.Protocol,
                webhookId,
                _options.MaxPayloadSizeBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WebhookPayloadTooLargeException)
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            RecordInboundValidation(false, "PayloadTooLarge");
            var errorMsg = $"Webhook payload size exceeds the maximum allowable limit of {_options.MaxPayloadSizeBytes} bytes.";
            LogValidationFailed(_logger, errorMsg);
            return Error.Validation("WebhookValidator.PayloadTooLarge", errorMsg);
        }
        finally
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0; // Reset for downstream model binding
            }
        }

        if (!isValid)
        {
            RecordInboundValidation(false, "InvalidSignature");
            LogValidationFailed(_logger, "Cryptographic HMAC-SHA256 signature verification failed.");
            return Error.Unauthorized("WebhookValidator.InvalidSignature", "Cryptographic HMAC-SHA256 signature verification failed.");
        }

        if (webhookId != null && _replayDetector != null)
        {
            var isUnique = await _replayDetector.TryRecordAsync(webhookId, cancellationToken).ConfigureAwait(false);
            if (!isUnique)
            {
                RecordInboundValidation(false, "ReplayDetected");
                var errorMsg = $"Webhook message with ID '{webhookId}' was already processed. Replay attack rejected.";
                LogValidationFailed(_logger, errorMsg);
                return Error.Unauthorized("WebhookValidator.ReplayDetected", errorMsg);
            }
        }

        RecordInboundValidation(true, "Success");
        return Result<bool>.Success(true);
    }

    private static void RecordInboundValidation(bool isValid, string reason)
    {
        WebhookDiagnostics.InboundValidationsTotal.Add(1,
            new KeyValuePair<string, object?>("is_valid", isValid),
            new KeyValuePair<string, object?>("reason", reason));
    }

    private static string? GetFirstNonEmptyValue(Microsoft.Extensions.Primitives.StringValues values)
    {
        foreach (var val in values)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        return null;
    }

    private static bool TryParseTimestamp(Microsoft.Extensions.Primitives.StringValues values, out long timestampSeconds)
    {
        foreach (var val in values)
        {
            if (!string.IsNullOrWhiteSpace(val) && long.TryParse(val.AsSpan().Trim(), out timestampSeconds))
            {
                return true;
            }
        }

        timestampSeconds = 0;
        return false;
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Inbound webhook request failed validation: {Reason}")]
    private static partial void LogValidationFailed(ILogger logger, string reason);
}
