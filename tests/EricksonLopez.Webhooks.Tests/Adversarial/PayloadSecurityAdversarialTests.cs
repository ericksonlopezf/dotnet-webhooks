// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Webhooks;

namespace EricksonLopez.Webhooks.Tests.Adversarial;

public class PayloadSecurityAdversarialTests
{
    [Fact]
    public async Task Payload_MassiveSize_ShouldBeBlockedBeforeDispatch()
    {
        // Arrange
        var options = new WebhookSenderOptions { MaxPayloadSizeBytes = 1024 * 1024 }; // 1MB
        using var httpClient = new System.Net.Http.HttpClient();
        var sender = new WebhookSender(httpClient, deadLetterQueue: null, options: options);
        var hugePayload = new string('A', 1024 * 1024 * 5); // 5MB payload
        
        // Act
        var message = new WebhookMessage(
            new Uri("https://example.com/webhook"),
            "whsec_test_secret",
            "event.large",
            Guid.NewGuid().ToString(),
            hugePayload);

        var result = await sender.SendAsync(message, CancellationToken.None);
        
        // Assert
        result.IsFailure.Should().BeTrue("Payloads exceeding MaxPayloadSizeBytes must be rejected before dispatch.");
        result.Error.Code.Should().Be("WebhookSender.PayloadTooLarge");
    }
}
