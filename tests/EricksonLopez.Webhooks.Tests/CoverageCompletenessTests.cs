// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed record SamplePayload(string Name, int Value);

[JsonSerializable(typeof(SamplePayload))]
internal sealed partial class SamplePayloadJsonContext : JsonSerializerContext
{
}

public sealed class CoverageCompletenessTests
{
    private const string TestSecret = "whsec_coverage_test_key_1234567890abcdef";
    private readonly Uri _validTarget = new("https://api.subscriber.com/webhooks");

    [Fact]
    public void WebhookPayloadTooLargeException_Constructors_SetExpectedProperties()
    {
        var ex1 = new WebhookPayloadTooLargeException(1024);
        ex1.MaxBytes.Should().Be(1024);
        ex1.Message.Should().Contain("1024");

        var ex2 = new WebhookPayloadTooLargeException("Custom message");
        ex2.Message.Should().Be("Custom message");
        ex2.MaxBytes.Should().Be(0);

        var inner = new InvalidOperationException("Inner error");
        var ex3 = new WebhookPayloadTooLargeException("Wrapper message", inner);
        ex3.Message.Should().Be("Wrapper message");
        ex3.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task WebhookSenderExtensions_SendAsync_ValidatesArgumentsAndDelivers()
    {
        var jsonTypeInfo = SamplePayloadJsonContext.Default.SamplePayload;

        var actNullSender = async () => await WebhookSenderExtensions.SendAsync<SamplePayload>(
            null!,
            _validTarget,
            TestSecret,
            "test.event",
            "evt-001",
            new SamplePayload("item", 42),
            jsonTypeInfo);

        await actNullSender.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("sender");

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient, options: new WebhookSenderOptions { EnableSsrfProtection = false });

        var actNullJsonInfo = async () => await WebhookSenderExtensions.SendAsync<SamplePayload>(
            sender,
            _validTarget,
            TestSecret,
            "test.event",
            "evt-001",
            new SamplePayload("item", 42),
            null!);

        await actNullJsonInfo.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("jsonTypeInfo");

        var result = await sender.SendAsync(
            _validTarget,
            TestSecret,
            "test.event",
            "evt-001",
            new SamplePayload("item", 42),
            jsonTypeInfo);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NoOpWebhookDeadLetterSink_EnqueueAsync_CompletesWithoutError()
    {
        var sink = new NoOpWebhookDeadLetterSink();
        var payload = new WebhookPayload("deliv-1", "order.created", "{}", DateTimeOffset.UtcNow, 1);
        var deliveryResult = new WebhookDeliveryResult(false, (int)HttpStatusCode.InternalServerError, 1, TimeSpan.FromMilliseconds(50), "Failed", null);

        var act = () => sink.EnqueueAsync(_validTarget, payload, deliveryResult);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void WebhookErrors_AllFactories_ProduceExpectedErrors()
    {
        var err1 = WebhookErrors.DeliveryFailed(_validTarget, 3, "Connection reset");
        err1.Code.Should().Be("WebhookSender.DeliveryFailed");
        err1.Description.Should().Contain("Connection reset");

        var err2 = WebhookErrors.DeliveryFailed(_validTarget, 2, null);
        err2.Code.Should().Be("WebhookSender.DeliveryFailed");
        err2.Description.Should().Contain("Unknown error");

        var err3 = WebhookErrors.SecurityViolation("Private IP blocked");
        err3.Code.Should().Be("WebhookSender.SecurityViolation");
        err3.Description.Should().Contain("Private IP blocked");

        var err4 = WebhookErrors.Timeout(_validTarget, TimeSpan.FromSeconds(15));
        err4.Code.Should().Be("WebhookSender.OverallTimeout");
        err4.Description.Should().Contain("15.0s");

        var err5 = WebhookErrors.InvalidConfiguration("Bad host");
        err5.Code.Should().Be("WebhookSender.InvalidConfiguration");
        err5.Description.Should().Be("Bad host");

        var err6 = WebhookErrors.PayloadTooLarge(65536);
        err6.Code.Should().Be("WebhookSender.PayloadTooLarge");
        err6.Description.Should().Contain("65536");
    }

    [Fact]
    public async Task InMemoryWebhookReplayDetector_FullLifecycle_CoversEdgeCases()
    {
        using var defaultDetector = new InMemoryWebhookReplayDetector();

        var actNull = async () => await defaultDetector.TryRecordAsync(null!);
        await actNull.Should().ThrowAsync<ArgumentException>();

        var actEmpty = async () => await defaultDetector.TryRecordAsync(string.Empty);
        await actEmpty.Should().ThrowAsync<ArgumentException>();

        var actWhitespace = async () => await defaultDetector.TryRecordAsync("   ");
        await actWhitespace.Should().ThrowAsync<ArgumentException>();

        var addedFirst = await defaultDetector.TryRecordAsync("id-1");
        addedFirst.Should().BeTrue();

        var addedDuplicate = await defaultDetector.TryRecordAsync("id-1");
        addedDuplicate.Should().BeFalse();

        // Eviction when capacity threshold reached
        var seenDict = (ConcurrentDictionary<string, long>)typeof(InMemoryWebhookReplayDetector)
            .GetField("_seenMessageIds", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(defaultDetector)!;
        var queue = (ConcurrentQueue<string>)typeof(InMemoryWebhookReplayDetector)
            .GetField("_insertionOrder", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(defaultDetector)!;

        // Force seen dictionary count to MaxCapacity (100,000)
        for (var i = 0; i < 100_000; i++)
        {
            seenDict.TryAdd($"dummy-{i}", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
        }
        queue.Enqueue("dummy-0");

        var addedOverCapacity = await defaultDetector.TryRecordAsync("id-new");
        addedOverCapacity.Should().BeTrue();

        // Cleanup expired entries
        var cleanupMethod = typeof(InMemoryWebhookReplayDetector).GetMethod(
            "CleanupExpiredEntries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        cleanupMethod.Should().NotBeNull();

        seenDict["expired-entry"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
        cleanupMethod!.Invoke(defaultDetector, [null]);
        seenDict.ContainsKey("expired-entry").Should().BeFalse();

        // Disposal
        defaultDetector.Dispose();
        defaultDetector.Dispose(); // second call triggers if (_disposed) return;

        var actAfterDispose = async () => await defaultDetector.TryRecordAsync("id-after-dispose");
        await actAfterDispose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void WebhookSecret_Operations_RedactsCorrectly()
    {
        var actNull = () => new WebhookSecret(null!);
        actNull.Should().Throw<ArgumentException>();

        var actWhitespace = () => new WebhookSecret("   ");
        actWhitespace.Should().Throw<ArgumentException>();

        var secret = new WebhookSecret("plain-text-secret");
        secret.GetUnsecuredString().Should().Be("plain-text-secret");
        secret.ToString().Should().Be("[REDACTED]");

        WebhookSecret implicitFromStr = "implicit-secret";
        implicitFromStr.GetUnsecuredString().Should().Be("implicit-secret");

        string implicitToStr = implicitFromStr;
        implicitToStr.Should().Be("implicit-secret");
    }

    [Fact]
    public void SafeSocketsHttpHandlerFactory_CreateAndIsPrivateOrLoopback_CoversAllBranches()
    {
        var unsecureOptions = new WebhookSenderOptions { EnableSsrfProtection = false };
        using var unsecureHandler = SafeSocketsHttpHandlerFactory.Create(unsecureOptions);
        unsecureHandler.AllowAutoRedirect.Should().BeFalse();

        var secureOptions = new WebhookSenderOptions { EnableSsrfProtection = true };
        using var secureHandler = SafeSocketsHttpHandlerFactory.Create(secureOptions);
        secureHandler.AllowAutoRedirect.Should().BeFalse();

        var method = typeof(SafeSocketsHttpHandlerFactory).GetMethod(
            "IsPrivateOrLoopback",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        bool CallIsPrivate(IPAddress ip) => (bool)method!.Invoke(null, [ip])!;

        // IPv6 LinkLocal and SiteLocal
        CallIsPrivate(IPAddress.Parse("fe80::1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("fec0::1")).Should().BeTrue();

        // IPv6 Unique Local: fd00::/8 and fc00::/7
        CallIsPrivate(IPAddress.Parse("fd00::1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("fc00::1")).Should().BeTrue();

        // IPv6 Public
        CallIsPrivate(IPAddress.Parse("2607:f8b0:4005:805::200e")).Should().BeFalse();

        // IPv4 Public
        CallIsPrivate(IPAddress.Parse("8.8.8.8")).Should().BeFalse();

        // IPv4 Special/Private ranges
        CallIsPrivate(IPAddress.Parse("0.0.0.0")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("10.1.2.3")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("172.16.0.1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("172.31.255.255")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("172.32.0.1")).Should().BeFalse();
        CallIsPrivate(IPAddress.Parse("192.168.1.1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("169.254.1.1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("127.0.0.1")).Should().BeTrue();
        CallIsPrivate(IPAddress.Parse("::ffff:127.0.0.1")).Should().BeTrue();
    }

    [Fact]
    public async Task SafeSocketsHttpHandlerFactory_ConnectCallback_ThrowsExpectedExceptions()
    {
        var optionsBlockPrivate = new WebhookSenderOptions { EnableSsrfProtection = true, AllowPrivateNetworks = false };
        using var handlerBlock = SafeSocketsHttpHandlerFactory.Create(optionsBlockPrivate);
        using var clientBlock = new HttpClient(handlerBlock);

        var actLoopback = async () => await clientBlock.GetAsync("http://127.0.0.1:54321");
        await actLoopback.Should().ThrowAsync<HttpRequestException>();

        var optionsAllowPrivate = new WebhookSenderOptions { EnableSsrfProtection = true, AllowPrivateNetworks = true };
        using var handlerAllow = SafeSocketsHttpHandlerFactory.Create(optionsAllowPrivate);
        using var clientAllow = new HttpClient(handlerAllow);

        var actRefused = async () => await clientAllow.GetAsync("http://127.0.0.1:59999");
        await actRefused.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void WebhookSenderOptions_Properties_ValidateCorrectly()
    {
        var options = new WebhookSenderOptions();

        var actZeroTimeout = () => options.Timeout = TimeSpan.Zero;
        actZeroTimeout.Should().Throw<ArgumentOutOfRangeException>();

        var actNegativeTimeout = () => options.Timeout = TimeSpan.FromSeconds(-1);
        actNegativeTimeout.Should().Throw<ArgumentOutOfRangeException>();

        options.Timeout = TimeSpan.FromSeconds(30);
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));

        var actZeroPayload = () => options.MaxPayloadSizeBytes = 0;
        actZeroPayload.Should().Throw<ArgumentOutOfRangeException>();

        var actNegativePayload = () => options.MaxPayloadSizeBytes = -100;
        actNegativePayload.Should().Throw<ArgumentOutOfRangeException>();

        options.MaxPayloadSizeBytes = 50000;
        options.MaxPayloadSizeBytes.Should().Be(50000);

        options.DangerousAllowInsecureHttp = true;
        options.DangerousAllowInsecureHttp.Should().BeTrue();
        options.SsrfProtection.RequireHttps.Should().BeFalse();

        options.DangerousAllowInsecureHttp = false;
        options.DangerousAllowInsecureHttp.Should().BeFalse();
        options.SsrfProtection.RequireHttps.Should().BeTrue();

        options.Protocol = WebhookSenderProtocol.Standard;
        options.Protocol.Should().Be(WebhookSenderProtocol.Standard);
    }

    [Fact]
    public void WebhookSigner_EdgeCases_CoverRentedBuffersAndProtocols()
    {
        // 1. Rented payload (> 4096 bytes)
        var largePayload = new string('A', 5000);
        var largeSig = WebhookSigner.ComputeSignature(TestSecret, 1700000000L, largePayload);
        largeSig.Should().StartWith("v1=");

        // 2. Rented secret (> 256 bytes) and rented content (> 4096 bytes)
        var largeSecret = new string('K', 300);
        var largeSig2 = WebhookSigner.ComputeSignature(largeSecret, 1700000000L, largePayload);
        largeSig2.Should().StartWith("v1=");

        // 3. StandardWebhooks with missing webhookId throws ArgumentException
        var actMissingId = () => WebhookSigner.ComputeSignature(
            TestSecret,
            1700000000L,
            Encoding.UTF8.GetBytes("{}"),
            WebhookSenderProtocol.Standard,
            null);
        actMissingId.Should().Throw<ArgumentException>();

        // 4. StandardWebhooks with valid webhookId
        var standardSig = WebhookSigner.ComputeSignature(
            TestSecret,
            1700000000L,
            Encoding.UTF8.GetBytes("{}"),
            WebhookSenderProtocol.Standard,
            "msg-standard-1");
        standardSig.Should().StartWith("v1,");

        // 5. VerifySignature with null or whitespace arguments returns false
        WebhookSigner.VerifySignature(null!, 1700000000L, "{}", "sha256=abc").Should().BeFalse();
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, "{}", null!).Should().BeFalse();
        WebhookSigner.VerifySignature("   ", 1700000000L, "{}", "sha256=abc").Should().BeFalse();
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, "{}", "   ").Should().BeFalse();
        WebhookSigner.VerifySignature(null!, 1700000000L, ReadOnlySpan<byte>.Empty, "v1=abc", WebhookReceiverProtocol.AutoDetect).Should().BeFalse();
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, ReadOnlySpan<byte>.Empty, null!, WebhookReceiverProtocol.AutoDetect).Should().BeFalse();

        // 6. VerifySignature with candidateCount > 5 returns false
        var tooManySignatures = "v1,s1 v1,s2 v1,s3 v1,s4 v1,s5 v1,s6";
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, "{}", tooManySignatures).Should().BeFalse();

        // 7. Space-delimited signatures where matching candidate succeeds
        var validSig = WebhookSigner.ComputeSignature(TestSecret, 1700000000L, "{}");
        var multiSig = $"sha256=bad1 sha256=bad2 {validSig}";
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, "{}", multiSig).Should().BeTrue();

        // 8. Large payload verification (rented buffer in VerifySignature)
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, largePayload, largeSig).Should().BeTrue();

        // 9. Standard protocol candidate without webhookId returns false
        WebhookSigner.VerifySignature(TestSecret, 1700000000L, "{}", "v1,some_sig", WebhookReceiverProtocol.Standard, null).Should().BeFalse();
    }

    [Fact]
    public async Task WebhookSigner_VerifySignatureAsync_EdgeCases()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        // Null secretKey overload with and without maxPayloadBytes returns false
        var res1 = await WebhookSigner.VerifySignatureAsync(
            secretKey: null!,
            1700000000L,
            stream,
            "sha256=abc",
            WebhookReceiverProtocol.AutoDetect);
        res1.Should().BeFalse();

        stream.Position = 0;
        var res2 = await WebhookSigner.VerifySignatureAsync(
            secretKey: null!,
            1700000000L,
            stream,
            "sha256=abc",
            WebhookReceiverProtocol.AutoDetect,
            webhookId: null,
            maxPayloadBytes: 1024);
        res2.Should().BeFalse();

        // Stream verification with large webhookId (> 256 chars) and large secretKey (> 256 chars)
        var largeSecret = new string('S', 300);
        var largeWebhookId = new string('W', 300);
        var sig = WebhookSigner.ComputeSignature(
            largeSecret,
            1700000000L,
            Encoding.UTF8.GetBytes("{}"),
            WebhookSenderProtocol.Standard,
            largeWebhookId);

        stream.Position = 0;
        var res3 = await WebhookSigner.VerifySignatureAsync(
            largeSecret,
            1700000000L,
            stream,
            sig,
            WebhookReceiverProtocol.Standard,
            largeWebhookId);
        res3.Should().BeTrue();
    }

    [Fact]
    public async Task InMemoryWebhookDeadLetterSink_ValidationAndProperties()
    {
        var actZero = () => new InMemoryWebhookDeadLetterSink(0);
        actZero.Should().Throw<ArgumentOutOfRangeException>();

        var actNegative = () => new InMemoryWebhookDeadLetterSink(-10);
        actNegative.Should().Throw<ArgumentOutOfRangeException>();

        var sink = new InMemoryWebhookDeadLetterSink(50);
        sink.MaxCapacity.Should().Be(50);

        var payload = new WebhookPayload("deliv-2", "order.created", "{}", DateTimeOffset.UtcNow, 1);
        var deliveryResult = new WebhookDeliveryResult(false, (int)HttpStatusCode.BadGateway, 1, TimeSpan.FromMilliseconds(10), "Bad Gateway", null);

        var actNullUrl = async () => await sink.EnqueueAsync(null!, payload, deliveryResult);
        await actNullUrl.Should().ThrowAsync<ArgumentNullException>();

        var actNullPayload = async () => await sink.EnqueueAsync(_validTarget, null!, deliveryResult);
        await actNullPayload.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WebhookSender_ConfigurationAndMetadataValidations()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient, options: new WebhookSenderOptions { EnableSsrfProtection = false, DangerousAllowInsecureHttp = false });

        // 1. Relative Target URL
        var relativeMsg = new WebhookMessage(new Uri("/relative/path", UriKind.Relative), TestSecret, "order.created", "evt-1", "{}");
        var resRelative = await sender.SendAsync(relativeMsg);
        resRelative.IsFailure.Should().BeTrue();
        resRelative.Error.Code.Should().Be("WebhookSender.InvalidConfiguration");

        // 2. Unsupported Scheme (ftp://)
        var ftpMsg = new WebhookMessage(new Uri("ftp://files.subscriber.com/webhook"), TestSecret, "order.created", "evt-2", "{}");
        var resFtp = await sender.SendAsync(ftpMsg);
        resFtp.IsFailure.Should().BeTrue();
        resFtp.Error.Code.Should().Be("WebhookSender.InvalidConfiguration");

        // 3. Insecure HTTP when DangerousAllowInsecureHttp = false
        var httpMsg = new WebhookMessage(new Uri("http://api.subscriber.com/webhook"), TestSecret, "order.created", "evt-3", "{}");
        var resHttp = await sender.SendAsync(httpMsg);
        resHttp.IsFailure.Should().BeTrue();
        resHttp.Error.Code.Should().Be("WebhookSender.SecurityViolation");

        // 4. Newline or carriage return in EventType
        var badEventType = new WebhookMessage(_validTarget, TestSecret, "order\ncreated", "evt-4", "{}");
        var resBadEvent = await sender.SendAsync(badEventType);
        resBadEvent.IsFailure.Should().BeTrue();
        resBadEvent.Error.Code.Should().Be("WebhookSender.InvalidConfiguration");

        // 5. Newline or carriage return in EventId
        var badEventId = new WebhookMessage(_validTarget, TestSecret, "order.created", "evt\r005", "{}");
        var resBadId = await sender.SendAsync(badEventId);
        resBadId.IsFailure.Should().BeTrue();
        resBadId.Error.Code.Should().Be("WebhookSender.InvalidConfiguration");

        // 6. Target URL with Query and Fragment is sanitized in error reporting
        var failHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var failClient = new HttpClient(failHandler);
        var sanitizingSender = new WebhookSender(failClient, options: new WebhookSenderOptions { EnableSsrfProtection = false });

        var urlWithQuery = new Uri("https://api.subscriber.com/webhooks?token=secret123#anchor");
        var msgWithQuery = new WebhookMessage(urlWithQuery, TestSecret, "order.created", "evt-query", "{}");
        var resSanitized = await sanitizingSender.SendAsync(msgWithQuery);

        resSanitized.IsFailure.Should().BeTrue();
        resSanitized.Error.Description.Should().Contain("https://api.subscriber.com/webhooks");
        resSanitized.Error.Description.Should().NotContain("secret123");

        // 7. Activity with TraceStateString propagates trace headers
        HttpRequestMessage? activityReq = null;
        var activityHandler = new MockHttpMessageHandler(req =>
        {
            activityReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var activityClient = new HttpClient(activityHandler);
        var activitySender = new WebhookSender(activityClient, options: new WebhookSenderOptions { EnableSsrfProtection = false });

        using var activity = new Activity("WebhookDeliveryTest").Start();
        activity.TraceStateString = "rojo=1,congo=2";

        var resActivity = await activitySender.SendAsync(new WebhookMessage(_validTarget, TestSecret, "order.created", "evt-trace", "{}"));
        resActivity.IsSuccess.Should().BeTrue();
        activityReq.Should().NotBeNull();
        activityReq!.Headers.Contains("traceparent").Should().BeTrue();
        activityReq.Headers.Contains("tracestate").Should().BeTrue();

        // 8. Mock response with null content
        var nullContentHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = null! });
        using var nullContentClient = new HttpClient(nullContentHandler);
        var nullContentSender = new WebhookSender(nullContentClient, options: new WebhookSenderOptions { EnableSsrfProtection = false });

        var resNullContent = await nullContentSender.SendAsync(new WebhookMessage(_validTarget, TestSecret, "order.created", "evt-null-content", "{}"));
        resNullContent.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task WebhookSigner_ComputeSignatureAsync_EdgeCases()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"test\":true}"));

        // Null secretKey
        var actNullSecret = async () => await WebhookSigner.ComputeSignatureAsync(null!, 1700000000L, stream, WebhookSenderProtocol.EricksonLopez);
        await actNullSecret.Should().ThrowAsync<ArgumentNullException>();

        // Null stream
        var actNullStream = async () => await WebhookSigner.ComputeSignatureAsync(TestSecret, 1700000000L, null!, WebhookSenderProtocol.EricksonLopez);
        await actNullStream.Should().ThrowAsync<ArgumentNullException>();

        // Standard protocol requires webhookId
        var actMissingId = async () => await WebhookSigner.ComputeSignatureAsync(TestSecret, 1700000000L, stream, WebhookSenderProtocol.Standard, webhookId: null);
        await actMissingId.Should().ThrowAsync<ArgumentException>();

        // Standard protocol with valid webhookId
        stream.Position = 0;
        var standardSig = await WebhookSigner.ComputeSignatureAsync(TestSecret, 1700000000L, stream, WebhookSenderProtocol.Standard, webhookId: "msg-std-1");
        standardSig.Should().StartWith("v1,");

        // EricksonLopez protocol
        stream.Position = 0;
        var ericksonSig = await WebhookSigner.ComputeSignatureAsync(TestSecret, 1700000000L, stream, WebhookSenderProtocol.EricksonLopez);
        ericksonSig.Should().StartWith("v1=");

        // maxPayloadBytes exceeded
        stream.Position = 0;
        var actTooLarge = async () => await WebhookSigner.ComputeSignatureAsync(TestSecret, 1700000000L, stream, WebhookSenderProtocol.EricksonLopez, maxPayloadBytes: 5);
        await actTooLarge.Should().ThrowAsync<WebhookPayloadTooLargeException>();
    }

    [Fact]
    public async Task WebhookSigner_VerifySignatureAsync_AllBranches()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"valid\":true}"));

        // Null secretKeys or null stream
        var r1 = await WebhookSigner.VerifySignatureAsync((string[])null!, 1700000000L, stream, "v1=abc", WebhookReceiverProtocol.AutoDetect, webhookId: null, maxPayloadBytes: null);
        r1.Should().BeFalse();

        var r2 = await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, null!, "v1=abc", WebhookReceiverProtocol.AutoDetect, webhookId: null, maxPayloadBytes: null);
        r2.Should().BeFalse();

        // Empty/whitespace signature
        var r3 = await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, stream, "   ", WebhookReceiverProtocol.AutoDetect, webhookId: null, maxPayloadBytes: null);
        r3.Should().BeFalse();

        // Candidates > 5
        var r4 = await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, stream, "v1,s1 v1,s2 v1,s3 v1,s4 v1,s5 v1,s6", WebhookReceiverProtocol.Standard, webhookId: "msg-1", maxPayloadBytes: null);
        r4.Should().BeFalse();

        // Protocol Standard but candidate does not match standard prefix or missing webhookId
        var r5 = await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, stream, "invalid_sig", WebhookReceiverProtocol.Standard, webhookId: null, maxPayloadBytes: null);
        r5.Should().BeFalse();

        // Stream exceeding maxPayloadBytes
        stream.Position = 0;
        var actExceeded = async () => await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, stream, "v1=abc", WebhookReceiverProtocol.AutoDetect, webhookId: null, maxPayloadBytes: 2);
        await actExceeded.Should().ThrowAsync<WebhookPayloadTooLargeException>();

        // Space-delimited signatures where second matches
        var sig = WebhookSigner.ComputeSignature(TestSecret, 1700000000L, "{\"valid\":true}");
        stream.Position = 0;
        var r6 = await WebhookSigner.VerifySignatureAsync(new[] { TestSecret }, 1700000000L, stream, $"v1=bad {sig}", WebhookReceiverProtocol.AutoDetect, webhookId: null, maxPayloadBytes: null);
        r6.Should().BeTrue();
    }

    [Fact]
    public async Task WebhookSender_PayloadBytesExceedingMaxPayload_ReturnsPayloadTooLarge()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(mockHandler);
        var sender = new WebhookSender(client, options: new WebhookSenderOptions { EnableSsrfProtection = false, MaxPayloadSizeBytes = 10 });

        var largeMemory = new ReadOnlyMemory<byte>(new byte[50]);
        var msg = new WebhookMessage(_validTarget, TestSecret, "order.created", "evt-large-bytes", largeMemory);

        var result = await sender.SendAsync(msg);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookSender.PayloadTooLarge");
    }

    private sealed class FaultyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 10;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Network read failed simulated exception");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("Network read failed simulated exception");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Network read failed simulated exception");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FaultyHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length)
        {
            length = 10;
            return true;
        }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new FaultyStream());
    }

    [Fact]
    public async Task WebhookSender_CaptureResponseSnippetAsync_CoversNullAndCatchBranches()
    {
        var method = typeof(WebhookSender).GetMethod(
            "CaptureResponseSnippetAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // 1. Response with null Content
        var respNull = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = null! };
        var taskNull = (Task<string?>)method!.Invoke(null, [respNull, CancellationToken.None])!;
        var resNull = await taskNull;
        resNull.Should().BeNull();

        // 2. Response with Faulty Content (throws on read)
        var respFaulty = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new FaultyHttpContent() };
        var taskFaulty = (Task<string?>)method.Invoke(null, [respFaulty, CancellationToken.None])!;
        var resFaulty = await taskFaulty;
        resFaulty.Should().BeNull();
    }
}

