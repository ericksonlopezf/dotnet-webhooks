// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class WebhookModelsAndDiagnosticsTests
{
    [Fact]
    public void WebhookPayload_Constructor_SetsPropertiesAndDefaultsAttemptNumberToOne()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payload = new WebhookPayload("delivery-123", "order.created", "{\"id\":1}", timestamp);

        payload.DeliveryId.Should().Be("delivery-123");
        payload.EventType.Should().Be("order.created");
        payload.Payload.Should().Be("{\"id\":1}");
        payload.Timestamp.Should().Be(timestamp);
        payload.AttemptNumber.Should().Be(1);
    }

    [Fact]
    public void WebhookPayload_CustomAttemptNumber_PreservesValue()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payload = new WebhookPayload("delivery-123", "order.created", "{\"id\":1}", timestamp, 4);

        payload.AttemptNumber.Should().Be(4);
    }

    [Fact]
    public void WebhookPayload_EqualityAndDeconstruction_BehavesAsExpected()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payload1 = new WebhookPayload("del-1", "evt", "{}", timestamp, 1);
        var payload2 = new WebhookPayload("del-1", "evt", "{}", timestamp, 1);
        var payload3 = payload1 with { AttemptNumber = 2 };

        payload1.Should().Be(payload2);
        payload1.Should().NotBe(payload3);

        var (id, type, data, time, attempt) = payload1;
        id.Should().Be("del-1");
        type.Should().Be("evt");
        data.Should().Be("{}");
        time.Should().Be(timestamp);
        attempt.Should().Be(1);
    }

    [Fact]
    public void WebhookDeliveryResult_Constructor_InitializesAllPropertiesAndDefaultsErrorToNull()
    {
        var duration = TimeSpan.FromMilliseconds(150);
        var result = new WebhookDeliveryResult(true, 200, 1, duration);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Attempts.Should().Be(1);
        result.Duration.Should().Be(duration);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void WebhookDeliveryResult_WithError_StoresErrorMessage()
    {
        var duration = TimeSpan.FromMilliseconds(300);
        var result = new WebhookDeliveryResult(false, 503, 3, duration, "Service Unavailable");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        result.Attempts.Should().Be(3);
        result.Duration.Should().Be(duration);
        result.ErrorMessage.Should().Be("Service Unavailable");
    }

    [Fact]
    public void WebhookDeliveryResult_EqualityAndValueSemantics_WorksCorrectly()
    {
        var duration = TimeSpan.FromMilliseconds(50);
        var r1 = new WebhookDeliveryResult(true, 200, 1, duration);
        var r2 = new WebhookDeliveryResult(true, 200, 1, duration);
        var r3 = new WebhookDeliveryResult(false, 500, 1, duration, "Server Error");

        (r1 == r2).Should().BeTrue();
        (r1 != r3).Should().BeTrue();
        r1.Equals(r2).Should().BeTrue();
        r1.Equals((object)r2).Should().BeTrue();
        r1.GetHashCode().Should().Be(r2.GetHashCode());
    }

    [Fact]
    public void WebhookHeaders_HeaderConstants_HaveExpectedValues()
    {
        WebhookHeaders.Signature.Should().Be("X-Webhook-Signature");
        WebhookHeaders.Timestamp.Should().Be("X-Webhook-Timestamp");
        WebhookHeaders.EventType.Should().Be("X-Webhook-Event");
        WebhookHeaders.DeliveryId.Should().Be("X-Webhook-Delivery-Id");

        WebhookHeaders.StandardSignature.Should().Be("webhook-signature");
        WebhookHeaders.StandardTimestamp.Should().Be("webhook-timestamp");
        WebhookHeaders.StandardDeliveryId.Should().Be("webhook-id");
        WebhookHeaders.StandardEventType.Should().Be("webhook-event");
    }

    [Fact]
    public void WebhookSenderProtocol_EnumValues_MatchExpectedOrdinals()
    {
        ((int)WebhookSenderProtocol.EricksonLopez).Should().Be(0);
        ((int)WebhookSenderProtocol.Standard).Should().Be(1);
    }

    [Fact]
    public void WebhookReceiverProtocol_EnumValues_MatchExpectedOrdinals()
    {
        ((int)WebhookReceiverProtocol.AutoDetect).Should().Be(0);
        ((int)WebhookReceiverProtocol.EricksonLopez).Should().Be(1);
        ((int)WebhookReceiverProtocol.Standard).Should().Be(2);
    }

    [Fact]
    public void WebhookDiagnostics_DiagnosticSourceNameAndVersion_MatchInvariants()
    {
        WebhookDiagnostics.DiagnosticSourceName.Should().Be("EricksonLopez.Webhooks");
        WebhookDiagnostics.ActivitySource.Name.Should().Be("EricksonLopez.Webhooks");
        WebhookDiagnostics.ActivitySource.Version.Should().Be("1.0.0");
        WebhookDiagnostics.Meter.Name.Should().Be("EricksonLopez.Webhooks");
        WebhookDiagnostics.Meter.Version.Should().Be("1.0.0");
        WebhookDiagnostics.DeliveriesTotal.Name.Should().Be("webhook.deliveries.total");
        WebhookDiagnostics.DeliveriesTotal.Description.Should().Be("Total number of outbound webhook HTTP delivery attempts.");
        WebhookDiagnostics.DeadLetterEscalationsTotal.Name.Should().Be("webhook.dead_letter.total");
        WebhookDiagnostics.DeadLetterEscalationsTotal.Description.Should().Be("Total number of failed webhook deliveries escalated to the dead-letter sink.");
        WebhookDiagnostics.DeliveryDuration.Name.Should().Be("webhook.delivery.duration");
        WebhookDiagnostics.DeliveryDuration.Unit.Should().Be("ms");
        WebhookDiagnostics.DeliveryDuration.Description.Should().Be("Duration of webhook HTTP delivery attempts in milliseconds.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WebhookMessage_StringConstructor_InvalidEventId_ThrowsArgumentException(string? invalidEventId)
    {
        var target = new Uri("https://example.com/webhook");
        var act = () => new WebhookMessage(target, "secret", "order.created", invalidEventId!, "{}");

        act.Should().Throw<ArgumentException>()
            .WithMessage("EventId cannot be null or empty. Provide a strict idempotency key.*")
            .WithParameterName("eventId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WebhookMessage_BytesConstructor_InvalidEventId_ThrowsArgumentException(string? invalidEventId)
    {
        var target = new Uri("https://example.com/webhook");
        var act = () => new WebhookMessage(target, "secret", "order.created", invalidEventId!, ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<ArgumentException>()
            .WithMessage("EventId cannot be null or empty. Provide a strict idempotency key.*")
            .WithParameterName("eventId");
    }

    [Fact]
    public void WebhookMessage_ToString_RedactsSecretAndFormatsCorrectly()
    {
        var target = new Uri("https://example.com/webhook");
        var msg = new WebhookMessage(target, "super-secret-key", "order.created", "evt-123", "{\"id\":1}");
        var str = msg.ToString();

        str.Should().Contain("SecretKey = [REDACTED]");
        str.Should().NotContain("super-secret-key");
        str.Should().EndWith(" }");
        str.Should().Be($"WebhookMessage {{ TargetUrl = {target}, SecretKey = [REDACTED], EventType = order.created, EventId = evt-123, PayloadString = {{\"id\":1}}, PayloadBytes =  }}");
    }
}
