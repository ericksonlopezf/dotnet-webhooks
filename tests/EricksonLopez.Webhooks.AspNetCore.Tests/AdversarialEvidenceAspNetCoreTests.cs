// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

/// <summary>
/// Empirical adversarial evidence tests for EricksonLopez.Webhooks.AspNetCore.
/// Reproduces security vulnerabilities, front-running attacks, and edge cases.
/// </summary>
public sealed class AdversarialEvidenceAspNetCoreTests
{
    private const string SecretKey = "whsec_inbound_test_secret_key_12345";

    [Fact]
    public async Task Finding_SEC_04_ReplayDetector_FrontRunning_Poisoning_Vulnerability()
    {
        // TARGET: WebhookValidator.ValidateRequestAsync
        // VULNERABILITY: TryRecordAsync is called BEFORE cryptographic signature verification.
        // An attacker can front-run and spoof a victim's message ID with a garbage signature,
        // poisoning the cache and permanently blocking the victim's authentic webhook.
        var replayDetector = new InMemoryWebhookReplayDetector(TimeSpan.FromMinutes(5));
        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = SecretKey,
            TimestampTolerance = TimeSpan.FromMinutes(5)
        });

        var validator = new WebhookValidator(options, TimeProvider.System, replayDetector, NullLogger<WebhookValidator>.Instance);

        var victimMessageId = "msg_order_998877";
        var legitimatePayload = "{\"orderId\":998877,\"amount\":500}";
        var legitimateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var legitimateSignature = WebhookSigner.ComputeSignature(SecretKey, legitimateTimestamp, legitimatePayload, WebhookSenderProtocol.Standard, victimMessageId);

        // ATTACK PHASE: Attacker sends an UNAUTHENTICATED request with victim's message ID and INVALID signature
        var attackContext = new DefaultHttpContext();
        attackContext.Request.Headers[WebhookHeaders.StandardDeliveryId] = victimMessageId;
        attackContext.Request.Headers[WebhookHeaders.StandardTimestamp] = legitimateTimestamp.ToString();
        attackContext.Request.Headers[WebhookHeaders.StandardSignature] = "v1,attacker_forged_invalid_signature";
        attackContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(legitimatePayload));

        var attackResult = await validator.ValidateRequestAsync(attackContext);

        // The attacker's request fails signature verification as expected
        attackResult.IsFailure.Should().BeTrue();
        attackResult.Error.Code.Should().Be("WebhookValidator.InvalidSignature");

        // LEGITIMATE PHASE: The legitimate sender now delivers the real, authentic webhook
        var legitContext = new DefaultHttpContext();
        legitContext.Request.Headers[WebhookHeaders.StandardDeliveryId] = victimMessageId;
        legitContext.Request.Headers[WebhookHeaders.StandardTimestamp] = legitimateTimestamp.ToString();
        legitContext.Request.Headers[WebhookHeaders.StandardSignature] = legitimateSignature;
        legitContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(legitimatePayload));

        var legitResult = await validator.ValidateRequestAsync(legitContext);

        // REMEDIATION VERIFIED: The legitimate webhook is now ACCEPTED because the attacker's invalid request
        // was rejected before it could poison the replay detector.
        legitResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Finding_SEC_05_WebhookValidator_OverflowException_On_LongMinValue()
    {
        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = SecretKey,
            TimestampTolerance = TimeSpan.FromMinutes(5)
        });

        var validator = new WebhookValidator(options, TimeProvider.System, null, NullLogger<WebhookValidator>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers[WebhookHeaders.Signature] = "v1=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        // Long.MinValue: -9223372036854775808
        context.Request.Headers[WebhookHeaders.Timestamp] = long.MinValue.ToString();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        // REMEDIATION VERIFIED: Math.Abs is avoided with unchecked subtraction.
        // It gracefully returns a TimestampExpired error instead of crashing the server.
        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.TimestampExpired");
    }

    [Fact]
    public async Task Finding_API_03_WebhookReceiverMiddleware_Rejects_Get_Requests_When_ValidatePostOnly_Is_False()
    {
        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = SecretKey,
            RoutePath = "/api/webhooks",
            ValidatePostOnly = false // Default
        });

        var validator = new WebhookValidator(options, TimeProvider.System, null, NullLogger<WebhookValidator>.Instance);

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new WebhookReceiverMiddleware(next, options, NullLogger<WebhookReceiverMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/webhooks";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, validator);

        // REMEDIATION VERIFIED: GET requests to /api/webhooks safely pass through
        // to downstream health checks or handlers without being intercepted by validation.
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
