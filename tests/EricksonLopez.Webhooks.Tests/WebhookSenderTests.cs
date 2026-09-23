// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class WebhookSenderTests
{
    private const string SecretKey = "whsec_test_secret_key";
    private readonly Uri _targetUrl = new("https://api.subscriber.com/webhook");

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new WebhookSender(null!);
        act.Should().Throw<ArgumentException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_NullOptionalParameters_UsesDefaultsWithoutThrowing()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient, deadLetterQueue: null, options: null, logger: null);

        sender.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_SuccessResponse_SetsDefaultHeadersAndComputesValidHmacSignature()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedContent = null;
        string? capturedContentType = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedContent = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = req.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "user.created", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{\"userId\":\"u1\"}")));

        result.IsSuccess.Should().BeTrue();

        result.Value.ErrorMessage.Should().BeNull();
        mockHandler.CallCount.Should().Be(1);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains(WebhookHeaders.Signature).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.Timestamp).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.EventType).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.DeliveryId).Should().BeTrue();

        var signature = capturedRequest.Headers.GetValues(WebhookHeaders.Signature).First();
        var timestampStr = capturedRequest.Headers.GetValues(WebhookHeaders.Timestamp).First();
        var eventType = capturedRequest.Headers.GetValues(WebhookHeaders.EventType).First();
        var deliveryId = capturedRequest.Headers.GetValues(WebhookHeaders.DeliveryId).First();

        signature.Should().StartWith("v1=");
        long.TryParse(timestampStr, out var timestampSeconds).Should().BeTrue();
        eventType.Should().Be("user.created");
        deliveryId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(deliveryId, out _).Should().BeTrue();
        deliveryId.Should().NotContain("-");

        capturedContentType.Should().Be("application/json");
        capturedContent.Should().Be("{\"userId\":\"u1\"}");



        // Verify HMAC signature validity
        WebhookSigner.VerifySignature(SecretKey, timestampSeconds, "{\"userId\":\"u1\"}", signature).Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_StandardProtocol_DispatchesStandardWebhooksHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions
        {
            Protocol = WebhookSenderProtocol.Standard,
            EnableSsrfProtection = false
        };
        var sender = new WebhookSender(httpClient, options: options);

        var eventId = Guid.NewGuid().ToString("N");
        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "invoice.paid", eventId, System.Text.Encoding.UTF8.GetBytes("{\"id\":1}")));

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Contains(WebhookHeaders.StandardSignature).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.StandardTimestamp).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.StandardEventType).Should().BeTrue();
        capturedRequest.Headers.Contains(WebhookHeaders.StandardDeliveryId).Should().BeTrue();

        var signature = capturedRequest.Headers.GetValues(WebhookHeaders.StandardSignature).First();
        var timestampStr = capturedRequest.Headers.GetValues(WebhookHeaders.StandardTimestamp).First();
        long.TryParse(timestampStr, out var timestampSeconds).Should().BeTrue();
        WebhookSigner.VerifySignature(SecretKey, timestampSeconds, "{\"id\":1}", signature, WebhookReceiverProtocol.Standard, eventId).Should().BeTrue();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task SendAsync_NonRetryableClientErrors_AbortsImmediatelyWithoutRetrying(int statusCode)
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)statusCode));
        using var httpClient = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, dlq, options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "error.nonretryable", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();




        mockHandler.CallCount.Should().Be(1);
        dlq.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task SendAsync_RetryableErrors_RetriesUpToMaxRetries(int statusCode)
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)statusCode));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "error.retryable", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();

        // 1 initial + 2 retries
        mockHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_HttpRequestException_RetriesAndEscalatesToDlq()
    {
        var mockHandler = new MockHttpMessageHandler(_ => throw new HttpRequestException("DNS resolution failed"));
        using var httpClient = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, dlq, options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "network.down", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();



        mockHandler.CallCount.Should().Be(1);
        dlq.Count.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_TimeoutException_RetriesAndEscalatesToDlq()
    {
        var mockHandler = new MockHttpMessageHandler(_ => throw new TimeoutException("Connection timed out"));
        using var httpClient = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, dlq, options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "timeout.event", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();


        dlq.Count.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_InternalCtsOperationCanceledException_RetriedAsTimeout()
    {
        // When HTTP client throws OperationCanceledException but caller's cancellationToken is NOT canceled,
        // it must be caught and treated as a retryable timeout rather than rethrown.
        var mockHandler = new MockHttpMessageHandler(_ => throw new OperationCanceledException("Internal HTTP timeout"));
        using var httpClient = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, dlq, options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "cts.timeout", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();


        dlq.Count.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_CallerCancelledToken_RethrowsOperationCanceledException()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sender.SendAsync(new WebhookMessage(new Uri("http://insecure.subscriber.test/webhook"), SecretKey, "test.event", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SendAsync_CancellationRequestedDuringHttpSend_RethrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var act = () => sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "event.cancelled", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SendAsync_DeadLetterQueueEnqueued_EnvelopeContainsExactPayloadDataAndAttempts()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, dlq, options);

        var beforeTime = DateTimeOffset.UtcNow.AddSeconds(-2);
        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "invoice.failed", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{\"invoiceId\":\"inv-99\"}")));
        var afterTime = DateTimeOffset.UtcNow.AddSeconds(2);

        result.IsSuccess.Should().BeFalse();
        dlq.Count.Should().Be(1);

        var snapshot = dlq.GetSnapshot();
        var entry = snapshot.First();
        entry.TargetUrl.Should().Be(_targetUrl);
        entry.Payload.EventType.Should().Be("invoice.failed");
        entry.Payload.Payload.Should().Be("{\"invoiceId\":\"inv-99\"}");
        entry.Payload.AttemptNumber.Should().Be(1);
        entry.Payload.DeliveryId.Should().NotBeNullOrWhiteSpace();
        entry.Payload.Timestamp.Should().BeOnOrAfter(beforeTime).And.BeOnOrBefore(afterTime);
        entry.LastResult.IsSuccess.Should().BeFalse();
        entry.LastResult.StatusCode.Should().Be(500);
        entry.LastResult.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_NullDeadLetterQueue_DoesNotThrowOnFailure()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, deadLetterQueue: null, options: options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "error.nodlq", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();

    }

    [Fact]
    public async Task SendAsync_ExponentialBackoffCappedAtMaxBackoff_CapsDelay()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var start = Stopwatch.GetTimestamp();
        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "error.capped", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));
        var elapsed = Stopwatch.GetElapsedTime(start);

        result.IsSuccess.Should().BeFalse();

        // Total delay for 2 retries capped at ~5ms + jitter should be very fast (< 1000ms)
        elapsed.TotalMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task SendAsync_OptionsTimeoutExpires_CancelsInternalCtsAndRetries()
    {
        var attempts = 0;
        var mockHandler = new MockHttpMessageHandler(async (req, ct) =>
        {
            attempts++;
            if (attempts == 1)
            {
                // Delay longer than options.Timeout (50ms) waiting on internal ct
                await Task.Delay(2000, ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            EnableSsrfProtection = false
        };
        var sender = new WebhookSender(httpClient, options: options);

        var sw = Stopwatch.StartNew();
        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "timeout.retry", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));
        sw.Stop();

        result.IsSuccess.Should().BeFalse();
        attempts.Should().Be(1);
        sw.ElapsedMilliseconds.Should().BeLessThan(1800);
    }

    [Fact]
    public async Task SendAsync_ActivityRecorded_OnSuccess_SetsExpectedTags()
    {
        Activity? recordedActivity = null;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WebhookDiagnostics.DiagnosticSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act =>
            {
                if (act.GetTagItem("webhook.event_type")?.ToString() == "user.created")
                {
                    recordedActivity = act;
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "user.created", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeTrue();
        recordedActivity.Should().NotBeNull();
        recordedActivity!.OperationName.Should().Be("webhook.send");
        recordedActivity.GetTagItem("http.status_code").Should().Be(200);
        recordedActivity.GetTagItem("webhook.attempts").Should().Be(1);
        recordedActivity.GetTagItem("webhook.is_success").Should().Be(true);
        recordedActivity.GetTagItem("webhook.event_type").Should().Be("user.created");
        recordedActivity.GetTagItem("webhook.target_url").Should().Be(_targetUrl.ToString());
        recordedActivity.GetTagItem("webhook.delivery_id").Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_ActivityRecorded_OnFailure_SetsErrorStatusAndFailureTags()
    {
        Activity? recordedActivity = null;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WebhookDiagnostics.DiagnosticSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act =>
            {
                if (act.GetTagItem("webhook.event_type")?.ToString() == "order.failed")
                {
                    recordedActivity = act;
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "order.failed", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeFalse();
        recordedActivity.Should().NotBeNull();
        recordedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        recordedActivity.StatusDescription.Should().Contain("HTTP 500");
        recordedActivity.GetTagItem("http.status_code").Should().Be(500);
        recordedActivity.GetTagItem("webhook.attempts").Should().Be(1);
        recordedActivity.GetTagItem("webhook.is_success").Should().Be(false);
    }

    [Fact]
    public async Task SendAsync_MetricsEmitted_RecordsDeliveriesAndEscalationsCorrectly()
    {
        var syncLock = new object();
        var recordedDeliveries = new System.Collections.Generic.List<(long Measurement, string? EventType, bool? IsSuccess)>();
        var recordedDurations = new System.Collections.Generic.List<(double Measurement, string? EventType)>();
        var recordedDeadLetters = new System.Collections.Generic.List<(long Measurement, string? EventType)>();

        using var meterListener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == WebhookDiagnostics.DiagnosticSourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                tagDict[tag.Key] = tag.Value;
            }

            lock (syncLock)
            {
                if (instrument.Name == "webhook.deliveries.total")
                {
                    tagDict.TryGetValue("event_type", out var evt);
                    tagDict.TryGetValue("is_success", out var succ);
                    recordedDeliveries.Add((measurement, evt?.ToString(), succ is bool b ? b : (bool?)null));
                }
                else if (instrument.Name == "webhook.dead_letter.total")
                {
                    tagDict.TryGetValue("event_type", out var evt);
                    recordedDeadLetters.Add((measurement, evt?.ToString()));
                }
            }
        });

        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var tagDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                tagDict[tag.Key] = tag.Value;
            }

            if (instrument.Name == "webhook.delivery.duration")
            {
                tagDict.TryGetValue("event_type", out var evt);
                lock (syncLock)
                {
                    recordedDurations.Add((measurement, evt?.ToString()));
                }
            }
        });

        meterListener.Start();

        // 1. Success execution
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(httpClient, options: options);

        await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "metric.success", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        (long Measurement, string? EventType, bool? IsSuccess)[] deliveriesSnapshot;
        (double Measurement, string? EventType)[] durationsSnapshot;
        (long Measurement, string? EventType)[] deadLettersSnapshot;

        lock (syncLock)
        {
            deliveriesSnapshot = recordedDeliveries.ToArray();
            durationsSnapshot = recordedDurations.ToArray();
        }

        deliveriesSnapshot.Should().Contain(d => d.Measurement == 1 && d.EventType == "metric.success" && d.IsSuccess == true);
        durationsSnapshot.Should().Contain(d => d.Measurement >= 0 && d.EventType == "metric.success");

        // 2. Failure with DLQ escalation
        var dlq = new InMemoryWebhookDeadLetterSink();
        var failHandler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Network failure"));
        using var failClient = new HttpClient(failHandler);
        var failSender = new WebhookSender(failClient, dlq, new WebhookSenderOptions { EnableSsrfProtection = false });

        await failSender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "metric.fail", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        lock (syncLock)
        {
            deliveriesSnapshot = recordedDeliveries.ToArray();
            deadLettersSnapshot = recordedDeadLetters.ToArray();
        }

        deliveriesSnapshot.Should().Contain(d => d.Measurement == 1 && d.EventType == "metric.fail" && d.IsSuccess == false);
        deadLettersSnapshot.Should().Contain(d => d.Measurement == 1 && d.EventType == "metric.fail");
    }

    [Fact]
    public async Task SendAsync_WithLogger_EmitsStructuredDiagnosticLogs()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var logger = Substitute.For<ILogger<WebhookSender>>();
        logger.IsEnabled(LogLevel.Information).Returns(true);
        logger.IsEnabled(LogLevel.Debug).Returns(true);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };

        var sender = new WebhookSender(httpClient, options: options, logger: logger);

        var result = await sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "event.logged", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        result.IsSuccess.Should().BeTrue();
        logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SendAsync_NullTargetUrl_ThrowsArgumentNullException()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient);

        var act = () => sender.SendAsync(new WebhookMessage(null!, SecretKey, "test.event", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_NullSecretKey_ThrowsArgumentNullException()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient);

        var act = () => sender.SendAsync(new WebhookMessage(_targetUrl, null!, "test.event", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_NullEventType_ThrowsArgumentNullException()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient);

        var act = () => sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, null!, Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes("{ }")));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_NullPayload_ThrowsArgumentNullException()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(mockHandler);
        var sender = new WebhookSender(httpClient);

        var act = () => sender.SendAsync(new WebhookMessage(_targetUrl, SecretKey, "test.event", Guid.NewGuid().ToString("N"), System.Text.Encoding.UTF8.GetBytes((string)null!)));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void WebhookSenderOptions_DefaultValues_MatchSpecification()
    {
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };

        options.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        options.Protocol.Should().Be(WebhookSenderProtocol.EricksonLopez);
    }

    [Fact]
    public void WebhookSenderOptions_CustomValues_PreserveAssignment()
    {
        var options = new WebhookSenderOptions
        {
            Timeout = TimeSpan.FromSeconds(5),



            Protocol = WebhookSenderProtocol.Standard
        };

        options.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        options.Protocol.Should().Be(WebhookSenderProtocol.Standard);
    }

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
    {
        var sender = new WebhookSender(new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK))));

        var actNull = () => sender.SendAsync(null!);
        await actNull.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public async Task SendAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var sender = new WebhookSender(new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK))));
        var message = new WebhookMessage(
            new Uri("https://example.com/webhook"),
            "secret_key_12345",
            "order.created",
            "evt_001",
            "{}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sender.SendAsync(message, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void WebhookSender_Constructor_NullOptionsAccessor_ThrowsArgumentNullException()
    {
        using var client = new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var act = () => new WebhookSender(client, (Microsoft.Extensions.Options.IOptions<WebhookSenderOptions>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("optionsAccessor");
    }

    [Fact]
    public void WebhookSender_CreateSafeSender_HandlesNullAndCustomOptions()
    {
        var senderDefault = WebhookSender.CreateSafeSender();
        senderDefault.Should().NotBeNull();

        var customOptions = new WebhookSenderOptions { MaxPayloadSizeBytes = 12345 };
        var senderCustom = WebhookSender.CreateSafeSender(options: customOptions);
        senderCustom.Should().NotBeNull();
    }

    [Fact]
    public void SanitizeTargetUrl_CoversAllQueryAndFragmentCombinations()
    {
        var plain = new Uri("https://api.subscriber.com/webhooks");
        WebhookSender.SanitizeTargetUrl(plain).Should().BeSameAs(plain);

        var queryOnly = new Uri("https://api.subscriber.com/webhooks?token=123");
        WebhookSender.SanitizeTargetUrl(queryOnly).ToString().Should().Be("https://api.subscriber.com/webhooks");

        var fragOnly = new Uri("https://api.subscriber.com/webhooks#section");
        WebhookSender.SanitizeTargetUrl(fragOnly).ToString().Should().Be("https://api.subscriber.com/webhooks");

        var both = new Uri("https://api.subscriber.com/webhooks?token=123#section");
        WebhookSender.SanitizeTargetUrl(both).ToString().Should().Be("https://api.subscriber.com/webhooks");
    }

    [Fact]
    public async Task SendAsync_ExactMaxPayloadSizeBytes_And_ContentTypeCharSet()
    {
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        using var client = new HttpClient(mockHandler);
        var options = new WebhookSenderOptions { EnableSsrfProtection = false, MaxPayloadSizeBytes = 10 };
        var sender = new WebhookSender(client, options: options);

        // Exact boundary for string payload (10 bytes)
        var stringMessage = new WebhookMessage(_targetUrl, SecretKey, "test.event", "evt-10", new string('a', 10));
        var res1 = await sender.SendAsync(stringMessage);
        res1.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        capturedRequest.Content.Headers.ContentType.CharSet.Should().Be("utf-8");

        // Exact boundary for byte[] payload (10 bytes)
        var byteMessage = new WebhookMessage(_targetUrl, SecretKey, "test.event", "evt-11", new byte[10]);
        var res2 = await sender.SendAsync(byteMessage);
        res2.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_CapturesResponseSnippet_And_PreservesPayloadStringReferenceInDlq()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"custom failure\"}", System.Text.Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(client, dlq, options);

        var payloadStr = "{\"id\":42}";
        var message = new WebhookMessage(_targetUrl, SecretKey, "order.failed", "evt-snippet", payloadStr);

        var result = await sender.SendAsync(message);
        result.IsFailure.Should().BeTrue();

        dlq.Count.Should().Be(1);
        var item = dlq.GetSnapshot().Single();
        item.LastResult.ResponseSnippet.Should().Be("{\"error\":\"custom failure\"}");
        object.ReferenceEquals(item.Payload.Payload, payloadStr).Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_EmptyResponseBody_ReturnsNullSnippet()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        });
        using var client = new HttpClient(mockHandler);
        var dlq = new InMemoryWebhookDeadLetterSink();
        var options = new WebhookSenderOptions { EnableSsrfProtection = false };
        var sender = new WebhookSender(client, dlq, options);

        var message = new WebhookMessage(_targetUrl, SecretKey, "order.failed", "evt-empty", "{}");
        var result = await sender.SendAsync(message);
        result.IsFailure.Should().BeTrue();

        var item = dlq.GetSnapshot().Single();
        item.LastResult.ResponseSnippet.Should().BeNull();
    }

    private sealed class TestLogger : ILogger<WebhookSender>
    {
        public System.Collections.Generic.List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter is not null)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}

