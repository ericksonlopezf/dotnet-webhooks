// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class InMemoryWebhookDeadLetterSinkTests
{
    [Fact]
    public void Count_EmptySink_ReturnsZero()
    {
        var sink = new InMemoryWebhookDeadLetterSink();

        sink.Count.Should().Be(0);
    }

    [Fact]
    public void GetSnapshot_EmptySink_ReturnsEmptyCollection()
    {
        var sink = new InMemoryWebhookDeadLetterSink();

        var snapshot = sink.GetSnapshot();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueAsync_ValidPayload_AddsToQueueAndIncrementsCount()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        var url = new Uri("https://example.com/webhook");
        var payload = new WebhookPayload("del-1", "order.created", "{}", DateTimeOffset.UtcNow, 1);
        var result = new WebhookDeliveryResult(false, 500, 3, TimeSpan.FromMilliseconds(100), "Server error");

        await sink.EnqueueAsync(url, payload, result);

        sink.Count.Should().Be(1);
        var snapshot = sink.GetSnapshot();
        snapshot.Should().HaveCount(1);
        var item = snapshot.Single();
        item.TargetUrl.Should().Be(url);
        item.Payload.DeliveryId.Should().Be("del-1");
        item.Payload.EventType.Should().Be("order.created");
        item.LastResult.StatusCode.Should().Be(500);
        item.LastResult.IsSuccess.Should().BeFalse();
        item.LastResult.ErrorMessage.Should().Be("Server error");
    }

    [Fact]
    public async Task EnqueueAsync_MultipleItems_PreservesOrderAndContents()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        var url1 = new Uri("https://example.com/webhook/1");
        var url2 = new Uri("https://example.com/webhook/2");
        var payload1 = new WebhookPayload("del-1", "item.1", "{}", DateTimeOffset.UtcNow, 1);
        var payload2 = new WebhookPayload("del-2", "item.2", "{}", DateTimeOffset.UtcNow, 2);
        var result = new WebhookDeliveryResult(false, 503, 1, TimeSpan.FromMilliseconds(50), "Service Unavailable");

        await sink.EnqueueAsync(url1, payload1, result);
        await sink.EnqueueAsync(url2, payload2, result);

        sink.Count.Should().Be(2);
        var snapshot = sink.GetSnapshot().ToList();
        snapshot[0].TargetUrl.Should().Be(url1);
        snapshot[0].Payload.DeliveryId.Should().Be("del-1");
        snapshot[1].TargetUrl.Should().Be(url2);
        snapshot[1].Payload.DeliveryId.Should().Be("del-2");
    }

    [Fact]
    public async Task EnqueueAsync_NullTargetUrl_ThrowsArgumentNullException()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        var payload = new WebhookPayload("del-1", "test", "{}", DateTimeOffset.UtcNow);
        var result = new WebhookDeliveryResult(false, 500, 1, TimeSpan.Zero);

        var act = () => sink.EnqueueAsync(null!, payload, result);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .WithParameterName("targetUrl");
    }

    [Fact]
    public async Task EnqueueAsync_NullPayload_ThrowsArgumentNullException()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        var url = new Uri("https://example.com/webhook");
        var result = new WebhookDeliveryResult(false, 500, 1, TimeSpan.Zero);

        var act = () => sink.EnqueueAsync(url, null!, result);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .WithParameterName("payload");
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentWriters_PreservesAllItemsAndCount()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        const int concurrentCount = 50;

        var tasks = Enumerable.Range(0, concurrentCount).Select(i =>
        {
            var url = new Uri($"https://example.com/webhook/{i}");
            var payload = new WebhookPayload($"del-{i}", "concurrent.event", "{}", DateTimeOffset.UtcNow, 1);
            var result = new WebhookDeliveryResult(false, 500, 1, TimeSpan.FromMilliseconds(10));
            return sink.EnqueueAsync(url, payload, result);
        });

        await Task.WhenAll(tasks);

        sink.Count.Should().Be(concurrentCount);
        var snapshot = sink.GetSnapshot();
        snapshot.Should().HaveCount(concurrentCount);
    }

    [Fact]
    public async Task GetSnapshot_SubsequentEnqueues_DoesNotMutatePreviouslyCapturedSnapshot()
    {
        var sink = new InMemoryWebhookDeadLetterSink();
        var url = new Uri("https://example.com/webhook/1");
        var payload1 = new WebhookPayload("del-1", "test.1", "{}", DateTimeOffset.UtcNow);
        var result = new WebhookDeliveryResult(false, 500, 1, TimeSpan.Zero);

        await sink.EnqueueAsync(url, payload1, result);
        var snapshotBefore = sink.GetSnapshot();

        var payload2 = new WebhookPayload("del-2", "test.2", "{}", DateTimeOffset.UtcNow);
        await sink.EnqueueAsync(url, payload2, result);
        var snapshotAfter = sink.GetSnapshot();

        snapshotBefore.Should().HaveCount(1);
        snapshotAfter.Should().HaveCount(2);
    }

    [Fact]
    public void InMemoryWebhookDeadLetterSink_ImplementsInterface()
    {
        var sink = new InMemoryWebhookDeadLetterSink();

        sink.Should().BeAssignableTo<IWebhookDeadLetterSink>();
    }
}
