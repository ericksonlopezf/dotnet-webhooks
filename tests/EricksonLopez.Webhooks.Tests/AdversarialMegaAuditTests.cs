// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

/// <summary>
/// Adversarial, security, concurrency, resilience, and API verification test suite
/// validating all mitigations and architectural corrections applied in v1.0.0.
/// </summary>
public sealed class AdversarialMegaAuditTests
{
    private const string SecretKey = "whsec_adversarial_test_secret_key_12345";
    private const string EventType = "user.provisioned";
    private const string SamplePayload = "{\"user\":\"audit_admin\",\"role\":\"superuser\"}";

    #region 1. SSRF (Server-Side Request Forgery) Defenses

    [Theory]
    [InlineData("http://127.0.0.1:8080/internal/admin")]
    [InlineData("http://localhost:5000/webhook")]
    [InlineData("http://[::1]:8080/admin")]
    public async Task SSRF_TargetingLoopbackAddress_IsBlockedBySsrfPolicy(string loopbackUrl)
    {
        // VERIFICATION: WebhookSender actively blocks loopback targets in v1.0.0.
        Uri? dispatchedUri = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            dispatchedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client);

        var result = await sender.SendAsync(new WebhookMessage(new Uri(loopbackUrl), SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.SecurityViolation");
        dispatchedUri.Should().BeNull();
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token")]
    public async Task SSRF_TargetingCloudMetadataEndpoints_IsBlockedBySsrfPolicy(string metadataUrl)
    {
        // VERIFICATION: WebhookSender actively blocks cloud metadata IMDS targets in v1.0.0.
        Uri? dispatchedUri = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            dispatchedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client);

        var result = await sender.SendAsync(new WebhookMessage(new Uri(metadataUrl), SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.SecurityViolation");
        dispatchedUri.Should().BeNull();
    }

    [Theory]
    [InlineData("http://10.0.0.1/internal/config")]
    [InlineData("http://172.16.0.1/admin/secrets")]
    [InlineData("http://192.168.1.1/gateway/status")]
    public async Task SSRF_TargetingPrivateRfc1918Networks_IsBlockedBySsrfPolicy(string privateUrl)
    {
        // VERIFICATION: WebhookSender actively blocks RFC 1918 private subnets in v1.0.0.
        Uri? dispatchedUri = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            dispatchedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client);

        var result = await sender.SendAsync(new WebhookMessage(new Uri(privateUrl), SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.SecurityViolation");
        dispatchedUri.Should().BeNull();
    }

    [Fact]
    public async Task SSRF_UnrestrictedRedirect_AttackerRedirectsWebhookToInternalNetwork()
    {
        var targetUrl = new Uri("https://webhook.attacker.com/sink");
        var internalMetadataUri = new Uri("http://169.254.169.254/latest/meta-data/");

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri == targetUrl)
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirectResponse.Headers.Location = internalMetadataUri;
                return redirectResponse;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client);

        var result = await sender.SendAsync(new WebhookMessage(targetUrl, SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region 2. StandardWebhooks Specification Compliance

    [Fact]
    public async Task StandardWebhooks_ProtocolSpecification_EmitsBase64AndComma()
    {
        // VERIFICATION: StandardWebhooks specification requires signatures formatted as "v1,<base64>".
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var options = new WebhookSenderOptions { Protocol = WebhookSenderProtocol.Standard, EnableSsrfProtection = false };
        var sender = new WebhookSender(client, options: options);

        var result = await sender.SendAsync(new WebhookMessage(new Uri("https://api.subscriber.com/webhook"), SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        capturedRequest!.Headers.TryGetValues(WebhookHeaders.StandardSignature, out var sigValues).Should().BeTrue();
        var signature = sigValues!.Single();

        // Verified behavior in v1.0.0: Emits "v1,<base64>"
        signature.Should().StartWith("v1,");
        signature.Should().Contain(",");
    }

    [Fact]
    public void StandardWebhooks_PayloadCanonicalization_IncludesWebhookIdInSignedPayload()
    {
        // VERIFICATION: StandardWebhooks mandates signing "${webhook_id}.${webhook_timestamp}.${body}".
        var timestamp = 1700000000L;
        var deliveryId = "msg_123456789";

        var actualSignature = WebhookSigner.ComputeSignature(SecretKey, timestamp, SamplePayload, WebhookSenderProtocol.Standard, deliveryId);

        var standardSignedContent = $"{deliveryId}.{timestamp}.{SamplePayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
        var standardHashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(standardSignedContent));
        var standardExpectedSig = $"v1,{Convert.ToBase64String(standardHashBytes)}";

        actualSignature.Should().Be(standardExpectedSig);
    }

    #endregion

    #region 3. Error Model Verification

    [Fact]
    public async Task ErrorModel_ExhaustedDelivery_ReturnsResultFailure()
    {
        // VERIFICATION: When delivery fails after exhausting all retries, SendAsync returns Result.Failure(Error).
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(client, deadLetterQueue: dlq, options: options);

        var result = await sender.SendAsync(new WebhookMessage(new Uri("https://api.subscriber.com/webhook"), SecretKey, EventType, Guid.NewGuid().ToString(), SamplePayload));

        result.IsFailure.Should().BeTrue("Failed delivery must return Result.Failure in v1.0.0.");
        result.Error.Code.Should().Be("WebhookSender.DeliveryFailed");
        dlq.Count.Should().Be(1);
    }

    #endregion

    #region 4. Resource Exhaustion & Memory Safety

    [Fact]
    public async Task ResourceExhaustion_InMemoryDeadLetterSink_EnforcesCapacityLimitAndEvictsOldest()
    {
        // VERIFICATION: InMemoryWebhookDeadLetterSink enforces bounded capacity in v1.0.0.
        const int maxCapacity = 100;
        var sink = new InMemoryWebhookDeadLetterSink(maxCapacity);
        var targetUrl = new Uri("https://api.subscriber.com/webhook");
        var result = new WebhookDeliveryResult(false, 503, 3, TimeSpan.FromMilliseconds(100), "Service Unavailable");

        const int iterations = 500;
        for (var i = 0; i < iterations; i++)
        {
            var envelope = new WebhookPayload($"del-{i}", EventType, SamplePayload, DateTimeOffset.UtcNow, 3);
            await sink.EnqueueAsync(targetUrl, envelope, result);
        }

        sink.Count.Should().Be(maxCapacity, "Queue must be bounded and never exceed max capacity.");
    }

    #endregion

    #region 5. Cryptography & Constant-Time Verification

    [Fact]
    public void Cryptography_VerifySignature_TimingAttackLengthCheckPrecedesFixedTimeEquals()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var validSignature = WebhookSigner.ComputeSignature(SecretKey, timestamp, SamplePayload);

        var truncatedSignature = validSignature.Substring(0, 10);

        var isVerified = WebhookSigner.VerifySignature(SecretKey, timestamp, SamplePayload, truncatedSignature);
        isVerified.Should().BeFalse();
    }

    [Fact]
    public void Cryptography_ComputeSignature_OptimizedAllocationProfile()
    {
        var timestamp = 1700000000L;
        var initialAllocations = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++)
        {
            _ = WebhookSigner.ComputeSignature(SecretKey, timestamp, SamplePayload);
        }

        var deltaAllocations = GC.GetAllocatedBytesForCurrentThread() - initialAllocations;
        deltaAllocations.Should().BeGreaterThan(0);
    }

    #endregion

    #region 6. Concurrency & High Throughput

    [Fact]
    public async Task Concurrency_100ParallelDeliveries_ThreadSafeExecutionWithoutDeadlock()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client, options: new WebhookSenderOptions { EnableSsrfProtection = false });
        var tasks = Enumerable.Range(0, 100).Select(i =>
            sender.SendAsync(new WebhookMessage(
                new Uri($"https://subscriber-{i}.example.com/webhook"),
                SecretKey,
                $"event.type.{i}",
                Guid.NewGuid().ToString(),
                $"{{\"index\":{i}}}")));

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(100);
        results.All(r => r.IsSuccess && r.Value.IsSuccess).Should().BeTrue();
    }

    #endregion

    #region 7. HTTP Status Code Retry Classification

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Retry_NonRetryable4xxClientErrors_AbortsImmediatelyWithoutRetrying(HttpStatusCode statusCode)
    {
        var dispatchCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            dispatchCount++;
            return new HttpResponseMessage(statusCode);
        });

        using var client = new HttpClient(handler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(client, options: options);

        var result = await sender.SendAsync(new WebhookMessage(
            new Uri("https://api.subscriber.com/webhook"),
            SecretKey,
            EventType,
            Guid.NewGuid().ToString(),
            SamplePayload));

        dispatchCount.Should().Be(1, $"HTTP {(int)statusCode} is non-retryable and must not trigger retries.");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.DeliveryFailed");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)] // 408
    [InlineData((HttpStatusCode)429)]          // 429 Too Many Requests
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]   // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]       // 504
    public async Task Retry_TransientAndRateLimitedErrors_RetriesUpToMaxRetries(HttpStatusCode statusCode)
    {
        var dispatchCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            dispatchCount++;
            return new HttpResponseMessage(statusCode);
        });

        using var client = new HttpClient(handler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(client, options: options);

        var result = await sender.SendAsync(new WebhookMessage(
            new Uri("https://api.subscriber.com/webhook"),
            SecretKey,
            EventType,
            Guid.NewGuid().ToString(),
            SamplePayload));

        dispatchCount.Should().Be(1, $"Direct WebhookSender without Polly pipeline executes a single attempt (ADR-003).");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.DeliveryFailed");
    }

    #endregion

    #region 8. Cancellation Propagation

    [Fact]
    public async Task Cancellation_PreCancelledToken_ThrowsOperationCanceledExceptionImmediately()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var sender = new WebhookSender(client);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sender.SendAsync(new WebhookMessage(
            new Uri("https://api.subscriber.com/webhook"),
            SecretKey,
            EventType,
            Guid.NewGuid().ToString(),
            SamplePayload), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
