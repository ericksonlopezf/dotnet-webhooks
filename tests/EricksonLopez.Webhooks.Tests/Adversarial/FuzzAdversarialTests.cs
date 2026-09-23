// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Webhooks;

namespace EricksonLopez.Webhooks.Tests.Adversarial;

public class FuzzAdversarialTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0")]
    [InlineData("{\"a\":1}")]
    [InlineData("!@#$%^&*()")]
    [InlineData("https://")]
    public async Task WebhookSender_FuzzingPayloads_ShouldNotCrash(string payload)
    {
        var options = new WebhookSenderOptions {  Timeout = TimeSpan.FromSeconds(1) };
        var sender = WebhookSender.CreateSafeSender(options: options);
        var target = new Uri("http://localhost:9999");
        
        // Act
        var result = await sender.SendAsync(new WebhookMessage(target, "secret", "event", Guid.NewGuid().ToString(), System.Text.Encoding.UTF8.GetBytes(payload)), CancellationToken.None);
        
        // Assert
        // The sender should safely fail because the endpoint is unavailable, NOT because it crashed trying to encode/sign the payload.
        result.IsFailure.Should().BeTrue();
    }
}
