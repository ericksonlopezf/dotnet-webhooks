// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Webhooks;

namespace EricksonLopez.Webhooks.Tests.Adversarial;

public class ConcurrencyAdversarialTests
{
    [Fact]
    public async Task WebhookSender_HighConcurrency_ShouldNotDeadlockOrLeak()
    {
        // Arrange
        var options = new WebhookSenderOptions { Timeout = TimeSpan.FromSeconds(2) };
        using var httpClient = new HttpClient();
        var sender = new WebhookSender(httpClient, deadLetterQueue: null, options: options);

        var targetUrl = new Uri("http://localhost:9999/dummy-endpoint");
        var payload = "{\"status\":\"test\"}";

        // Act
        var tasks = Enumerable.Range(0, 1000).Select(i =>
            sender.SendAsync(new WebhookMessage(targetUrl, "secret-key", "event.concurrent", $"evt-{i}", System.Text.Encoding.UTF8.GetBytes(payload)), CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Length.Should().Be(1000);
        // We expect failures because the port is likely closed or unreachable
        results.All(r => r.IsFailure).Should().BeTrue();
    }
}
