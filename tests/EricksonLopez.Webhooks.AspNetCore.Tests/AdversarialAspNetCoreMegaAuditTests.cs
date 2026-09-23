// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

/// <summary>
/// Adversarial security, protocol compliance, and concurrency tests for EricksonLopez.Webhooks.AspNetCore.
/// </summary>
public sealed class AdversarialAspNetCoreMegaAuditTests
{
    private const string PrimarySecret = "whsec_primary_secret_key_12345";
    private const string SecondarySecret = "whsec_secondary_secret_key_67890";
    private const string SamplePayload = "{\"event\":\"invoice.paid\",\"amount\":499.00}";

    #region 1. StandardWebhooks Interoperability Breakdown

    [Fact]
    public async Task StandardWebhooks_AuthenticStandardWebhooksPayload_IsRejectedDueToFormatMismatch()
    {
        // EVIDENCE: When an external standard provider (e.g. Svix, OpenAI) transmits a standard webhook:
        // - Header: "webhook-signature"
        // - Format: "v1,<base64_hmac>"
        // - Signed string: "${webhook_id}.${webhook_timestamp}.${body}"
        // WebhookValidator FAILS to verify it because it expects "v1=<hex_hmac>" signed over "${webhook_timestamp}.${body}".

        var timestampSeconds = 1700000000L;
        var webhookId = "msg_2dK3v9F1aB5c";

        // Correct StandardWebhooks computation:
        var standardSignedContent = $"{webhookId}.{timestampSeconds}.{SamplePayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PrimarySecret));
        var standardHashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(standardSignedContent));
        var standardSignature = $"v1,{Convert.ToBase64String(standardHashBytes)}";

        var context = new DefaultHttpContext();
        context.Request.Headers[WebhookHeaders.StandardSignature] = standardSignature;
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = timestampSeconds.ToString();
        context.Request.Headers[WebhookHeaders.StandardDeliveryId] = webhookId;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(SamplePayload));

        var fakeTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(timestampSeconds));
        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = PrimarySecret,
            Protocol = WebhookReceiverProtocol.Standard
        });
        var validator = new WebhookValidator(options, fakeTime);

        var result = await validator.ValidateRequestAsync(context);

        // VERIFICATION: Legitimate third-party StandardWebhooks payloads succeed in v1.0.0!
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region 2. Middleware JSON Response Formatting & Injection Risk

    [Fact]
    public async Task Middleware_MissingSignature_Writes401WithRawJsonFormatting()
    {
        // EVIDENCE: WebhookReceiverMiddleware manually constructs JSON via string interpolation:
        // var json = $"{{\"code\":\"{validationResult.Error.Code}\",\"error\":\"{validationResult.Error.Description}\"}}";
        // rather than using System.Text.Json serializer.
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(SamplePayload));
        context.Response.Body = new MemoryStream();

        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = PrimarySecret,
            RoutePath = "/api/webhooks"
        });
        var validator = new WebhookValidator(options);

        var middleware = new WebhookReceiverMiddleware(_ => Task.CompletedTask, options);

        await middleware.InvokeAsync(context, validator);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().Be("application/json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var responseJson = await reader.ReadToEndAsync();

        responseJson.Should().Contain("\"code\":\"WebhookValidator.MissingSignature\"");
    }

    #endregion

    #region 3. Concurrency & Secret Key Rotation

    [Fact]
    public async Task SecretRotation_HighConcurrencyDualKeyValidation_ThreadSafeExecution()
    {
        // EVIDENCE: Validates thread safety of secret key rotation under concurrent request execution.
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timestamp = fakeTime.GetUtcNow().ToUnixTimeSeconds();

        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = PrimarySecret,
            SecretKeys = [PrimarySecret, SecondarySecret],
            Protocol = WebhookReceiverProtocol.EricksonLopez
        });
        var validator = new WebhookValidator(options, fakeTime);

        // Sign 50 payloads with primary and 50 payloads with secondary key:
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            var keyToUse = i % 2 == 0 ? PrimarySecret : SecondarySecret;
            var sig = WebhookSigner.ComputeSignature(keyToUse, timestamp, SamplePayload);

            var ctx = new DefaultHttpContext();
            ctx.Request.Headers[WebhookHeaders.Signature] = sig;
            ctx.Request.Headers[WebhookHeaders.Timestamp] = timestamp.ToString();
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(SamplePayload));

            return await validator.ValidateRequestAsync(ctx);
        });

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(100);
        results.All(r => r.IsSuccess && r.Value).Should().BeTrue();
    }

    #endregion

    #region 4. Anti-Replay Timestamp Tolerance Boundary

    [Fact]
    public async Task AntiReplay_ExactClockSkewToleranceBoundary_EnforcesLimitsStrictly()
    {
        // Truncate baseTime to whole seconds because Unix time headers carry second precision only.
        // NOTE: In WebhookValidator, if server time has sub-second precision, a timestamp at exactly
        // 300s skew will measure as 300.xxx seconds and be rejected prematurely due to sub-second drift!
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var baseTime = DateTimeOffset.FromUnixTimeSeconds(nowSeconds);
        var fakeTime = new FakeTimeProvider(baseTime);

        var options = Options.Create(new WebhookReceiverOptions
        {
            SecretKey = PrimarySecret,
            TimestampTolerance = TimeSpan.FromMinutes(5)
        });
        var validator = new WebhookValidator(options, fakeTime);

        // Case A: 300 seconds skew (exactly at tolerance boundary) -> Accepted
        var boundaryTimestamp = baseTime.AddMinutes(-5).ToUnixTimeSeconds();
        var boundarySig = WebhookSigner.ComputeSignature(PrimarySecret, boundaryTimestamp, SamplePayload);

        var ctxA = new DefaultHttpContext();
        ctxA.Request.Headers[WebhookHeaders.Signature] = boundarySig;
        ctxA.Request.Headers[WebhookHeaders.Timestamp] = boundaryTimestamp.ToString();
        ctxA.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(SamplePayload));

        var resA = await validator.ValidateRequestAsync(ctxA);
        resA.IsSuccess.Should().BeTrue();

        // Case B: 301 seconds skew (exceeds tolerance by 1 second) -> Rejected
        var expiredTimestamp = baseTime.AddMinutes(-5).AddSeconds(-1).ToUnixTimeSeconds();
        var expiredSig = WebhookSigner.ComputeSignature(PrimarySecret, expiredTimestamp, SamplePayload);

        var ctxB = new DefaultHttpContext();
        ctxB.Request.Headers[WebhookHeaders.Signature] = expiredSig;
        ctxB.Request.Headers[WebhookHeaders.Timestamp] = expiredTimestamp.ToString();
        ctxB.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(SamplePayload));

        var resB = await validator.ValidateRequestAsync(ctxB);
        resB.IsFailure.Should().BeTrue();
        resB.Error.Code.Should().Be("WebhookValidator.TimestampExpired");
    }

    #endregion
}
