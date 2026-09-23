// Copyright © Erickson Lopez. MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.Redis;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Webhooks.Redis.Tests;

public sealed class RedisWebhookTests
{
    [Fact]
    public void Constructor_WithNullConnectionMultiplexer_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RedisWebhookReplayDetector(null!, TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentNullException>(() => new RedisWebhookDeadLetterSink(null!));
    }

    [Fact]
    public void Constructor_WithNullOrWhitespaceListKey_ThrowsArgumentException()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        // Act & Assert
        var ex1 = Assert.Throws<ArgumentException>(() => new RedisWebhookDeadLetterSink(multiplexer, null!));
        ex1.Message.Should().StartWith("List key cannot be null or whitespace.");
        ex1.ParamName.Should().Be("listKey");

        var ex2 = Assert.Throws<ArgumentException>(() => new RedisWebhookDeadLetterSink(multiplexer, "   "));
        ex2.Message.Should().StartWith("List key cannot be null or whitespace.");
        ex2.ParamName.Should().Be("listKey");
    }

    [Fact]
    public async Task TryRecordAsync_WithNullOrWhitespaceMessageId_ReturnsFalse()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var detector = new RedisWebhookReplayDetector(multiplexer, TimeSpan.FromMinutes(5));

        // Act
        var nullResult = await detector.TryRecordAsync(null!);
        var emptyResult = await detector.TryRecordAsync(string.Empty);
        var whitespaceResult = await detector.TryRecordAsync("   ");

        // Assert
        nullResult.Should().BeFalse();
        emptyResult.Should().BeFalse();
        whitespaceResult.Should().BeFalse();
    }

    [Fact]
    public async Task TryRecordAsync_WhenKeyNotExists_ReturnsTrue()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase().Returns(database);

        database.StringSetAsync(
            (RedisKey)"webhook:replay:msg_123",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(5),
            When.NotExists)
            .Returns(true);

        var detector = new RedisWebhookReplayDetector(multiplexer, TimeSpan.FromMinutes(5));

        // Act
        var result = await detector.TryRecordAsync("msg_123");

        // Assert
        result.Should().BeTrue();
        await database.Received(1).StringSetAsync(
            (RedisKey)"webhook:replay:msg_123",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(5),
            When.NotExists);
    }

    [Fact]
    public async Task TryRecordAsync_WhenKeyAlreadyExists_ReturnsFalse()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase().Returns(database);

        database.StringSetAsync(
            (RedisKey)"webhook:replay:msg_duplicate",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(5),
            When.NotExists)
            .Returns(false);

        var detector = new RedisWebhookReplayDetector(multiplexer, TimeSpan.FromMinutes(5));

        // Act
        var result = await detector.TryRecordAsync("msg_duplicate");

        // Assert
        result.Should().BeFalse();
        await database.Received(1).StringSetAsync(
            (RedisKey)"webhook:replay:msg_duplicate",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(5),
            When.NotExists);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var sink = new RedisWebhookDeadLetterSink(multiplexer);
        var targetUri = new Uri("https://example.com/webhook");
        var payload = new WebhookPayload("evt_1", "order.created", "{}", DateTimeOffset.UtcNow);
        var deliveryResult = new WebhookDeliveryResult(false, 500, 3, TimeSpan.FromMilliseconds(50), "Error");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.EnqueueAsync(null!, payload, deliveryResult));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.EnqueueAsync(targetUri, null!, deliveryResult));
    }

    [Fact]
    public async Task EnqueueAsync_WithValidArguments_PushesToTailOfRedisList()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var sink = new RedisWebhookDeadLetterSink(multiplexer, "custom:dlq");
        var targetUri = new Uri("https://example.com/webhook");
        var payload = new WebhookPayload("evt_1", "order.created", "{}", DateTimeOffset.UtcNow);
        var deliveryResult = new WebhookDeliveryResult(false, 500, 3, TimeSpan.FromMilliseconds(50), "Error");

        // Act
        await sink.EnqueueAsync(targetUri, payload, deliveryResult);

        // Assert
        await database.Received(1).ListRightPushAsync(
            Arg.Is<RedisKey>(k => k == "custom:dlq"),
            Arg.Is<RedisValue>(v => v.ToString().Contains("https://example.com/webhook") && v.ToString().Contains("evt_1")),
            Arg.Is<When>(w => w == When.Always),
            Arg.Is<CommandFlags>(f => f == CommandFlags.None));
    }

    [Fact]
    public async Task AddRedisWebhookReplayDetector_RegistersServiceCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase().Returns(database);
        services.AddSingleton(multiplexer);

        // Act (explicit retention)
        services.AddRedisWebhookReplayDetector(TimeSpan.FromMinutes(15));
        var provider = services.BuildServiceProvider();
        var detector = provider.GetService<IWebhookReplayDetector>();

        // Assert
        detector.Should().NotBeNull();
        detector.Should().BeOfType<RedisWebhookReplayDetector>();

        await detector!.TryRecordAsync("msg_custom");
        await database.Received(1).StringSetAsync(
            (RedisKey)"webhook:replay:msg_custom",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(15),
            When.NotExists);

        // Act (default retention)
        var servicesDefault = new ServiceCollection();
        servicesDefault.AddSingleton(multiplexer);
        servicesDefault.AddRedisWebhookReplayDetector();
        var providerDefault = servicesDefault.BuildServiceProvider();
        var detectorDefault = providerDefault.GetService<IWebhookReplayDetector>();

        detectorDefault.Should().NotBeNull();
        await detectorDefault!.TryRecordAsync("msg_default");
        await database.Received(1).StringSetAsync(
            (RedisKey)"webhook:replay:msg_default",
            Arg.Any<RedisValue>(),
            TimeSpan.FromMinutes(10),
            When.NotExists);
    }

    [Fact]
    public async Task AddRedisWebhookDeadLetterSink_RegistersServiceCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        services.AddSingleton(multiplexer);

        // Act (custom key)
        services.AddRedisWebhookDeadLetterSink("custom:dlq:key");
        var provider = services.BuildServiceProvider();
        var sink = provider.GetService<IWebhookDeadLetterSink>();

        // Assert
        sink.Should().NotBeNull();
        sink.Should().BeOfType<RedisWebhookDeadLetterSink>();

        var targetUri = new Uri("https://example.com/webhook");
        var payload = new WebhookPayload("evt_1", "order.created", "{}", DateTimeOffset.UtcNow);
        var deliveryResult = new WebhookDeliveryResult(false, 500, 3, TimeSpan.FromMilliseconds(50), "Error");
        await sink!.EnqueueAsync(targetUri, payload, deliveryResult);

        await database.Received(1).ListRightPushAsync(
            (RedisKey)"custom:dlq:key",
            Arg.Any<RedisValue>(),
            When.Always,
            CommandFlags.None);

        // Act (default key)
        var servicesDefault = new ServiceCollection();
        servicesDefault.AddSingleton(multiplexer);
        servicesDefault.AddRedisWebhookDeadLetterSink();
        var providerDefault = servicesDefault.BuildServiceProvider();
        var sinkDefault = providerDefault.GetService<IWebhookDeadLetterSink>();

        sinkDefault.Should().NotBeNull();
        await sinkDefault!.EnqueueAsync(targetUri, payload, deliveryResult);

        await database.Received(1).ListRightPushAsync(
            (RedisKey)"webhook:dlq",
            Arg.Any<RedisValue>(),
            When.Always,
            CommandFlags.None);
    }
}
