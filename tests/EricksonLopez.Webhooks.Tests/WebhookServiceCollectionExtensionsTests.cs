// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class WebhookServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWebhooks_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddWebhooks();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddWebhooks_DefaultRegistration_RegistersAllRequiredServicesAndDefaults()
    {
        var services = new ServiceCollection();
        var returnedServices = services.AddWebhooks();

        returnedServices.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        // Validate WebhookSenderOptions singleton registration
        var options = provider.GetService<WebhookSenderOptions>();
        options.Should().NotBeNull();
        options!.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        options.Protocol.Should().Be(WebhookSenderProtocol.EricksonLopez);

        // Validate IWebhookDeadLetterSink singleton registration
        var dlq = provider.GetService<IWebhookDeadLetterSink>();
        dlq.Should().NotBeNull();
        dlq.Should().BeOfType<NoOpWebhookDeadLetterSink>();

        // Validate IWebhookSender typed client registration
        var sender = provider.GetService<IWebhookSender>();
        sender.Should().NotBeNull();
        sender.Should().BeOfType<WebhookSender>();
    }

    [Fact]
    public void AddWebhooks_WithConfigurationCallback_ConfiguresCustomOptions()
    {
        var services = new ServiceCollection();
        services.AddWebhooks(opts =>
        {
            opts.Timeout = TimeSpan.FromSeconds(15);
            opts.Protocol = WebhookSenderProtocol.Standard;
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<WebhookSenderOptions>();
        options.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        options.Protocol.Should().Be(WebhookSenderProtocol.Standard);
    }
}
