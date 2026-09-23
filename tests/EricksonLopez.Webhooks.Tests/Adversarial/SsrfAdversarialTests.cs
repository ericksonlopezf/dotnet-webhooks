// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Webhooks;
using EricksonLopez.Security.Network;

namespace EricksonLopez.Webhooks.Tests.Adversarial;

public class SsrfAdversarialTests
{
    private readonly WebhookSender _safeSender;
    
    public SsrfAdversarialTests()
    {
        var options = new WebhookSenderOptions
        {
            EnableSsrfProtection = true,
            AllowPrivateNetworks = false,
            Timeout = TimeSpan.FromSeconds(2)
        };
        options.SsrfProtection.RequireHttps = true;
        
        _safeSender = WebhookSender.CreateSafeSender(options: options);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("https://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://[::1]")]
    [InlineData("https://[::1]")]
    [InlineData("http://2130706433")] // 127.0.0.1 in decimal
    [InlineData("http://0x7F000001")]  // 127.0.0.1 in hex
    [InlineData("http://169.254.169.254")] // AWS metadata
    [InlineData("http://metadata.google.internal")] // GCP metadata
    [InlineData("http://192.168.1.1")] // Private IPv4
    [InlineData("http://10.0.0.1")] // Private IPv4
    [InlineData("http://172.16.0.1")] // Private IPv4
    [InlineData("http://[fd00::1]")] // Unique local IPv6
    public async Task Ssrf_AttemptedPayloadDelivery_ToProtectedTargets_ShouldReturnSecurityViolation(string targetUrl)
    {
        // Arrange
        var uri = new Uri(targetUrl);
        var payload = "{\"malicious\":true}";
        var message = new WebhookMessage(uri, "test-secret", "test.event", Guid.NewGuid().ToString(), payload);
        
        // Act
        var result = await _safeSender.SendAsync(message, CancellationToken.None);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("WebhookSender.SecurityViolation", "WebhookSender.InvalidConfiguration");
        
        // Ensure no exception is thrown out of band, must be captured in Result
    }
}
