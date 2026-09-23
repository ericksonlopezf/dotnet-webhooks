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

        var ex = act.Should().Throw<ArgumentNullException>().WithParameterName("services").Which;
        ex.StackTrace.Should().NotContain("OptionsServiceCollectionExtensions");
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

        // Validate IWebhookSender typed client registration and named HttpClient
        var factory = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>();
        var client = factory.CreateClient("EricksonLopez.Webhooks");
        client.Should().NotBeNull();

        var optionsMonitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>();
        var namedOptions = optionsMonitor.Get("EricksonLopez.Webhooks");
        namedOptions.HttpMessageHandlerBuilderActions.Should().NotBeEmpty();

        var sender = provider.GetService<IWebhookSender>();
        sender.Should().NotBeNull();
        sender.Should().BeOfType<WebhookSender>();

        var senderClient = (System.Net.Http.HttpClient)typeof(WebhookSender).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sender)!;
        senderClient.Should().NotBeNull();
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

    [Fact]
    public void AddWebhooks_InvalidTimeoutViaPostConfigure_ThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddWebhooks();
        services.PostConfigure<WebhookSenderOptions>(o =>
        {
            typeof(WebhookSenderOptions).GetField("_timeout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(o, TimeSpan.Zero);
        });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<WebhookSenderOptions>();
        var ex = act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().Which;
        ex.Failures.Should().Contain(f => f.Contains("Timeout must be greater than zero."));
    }

    [Fact]
    public void AddWebhooks_InvalidPayloadSizeViaPostConfigure_ThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddWebhooks();
        services.PostConfigure<WebhookSenderOptions>(o =>
        {
            typeof(WebhookSenderOptions).GetField("_maxPayloadSizeBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(o, 0);
        });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<WebhookSenderOptions>();
        var ex = act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().Which;
        ex.Failures.Should().Contain(f => f.Contains("MaxPayloadSizeBytes must be greater than zero."));
    }

    [Fact]
    public async Task AddWebhooks_StandardResilienceHandler_RetryPredicate_HandlesExpectedStatusesAndExceptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebhooks();

        using var provider = services.BuildServiceProvider();
        var resilienceMonitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>>();
        var resilienceOptions = resilienceMonitor.Get("EricksonLopez.Webhooks-standard");
        var predicate = resilienceOptions.Retry.ShouldHandle;
        predicate.Should().NotBeNull();

        // 1. Exception is not null
        var exArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromException<System.Net.Http.HttpResponseMessage>(new System.Net.Http.HttpRequestException()),
            0);
        (await predicate!(exArgs)).Should().BeTrue();

        // 2. HTTP 500 InternalServerError
        var serverErrorArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)),
            0);
        (await predicate(serverErrorArgs)).Should().BeTrue();

        // 3. HTTP 502 BadGateway
        var badGatewayArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)),
            0);
        (await predicate(badGatewayArgs)).Should().BeTrue();

        // 4. HTTP 408 RequestTimeout
        var timeoutArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout)),
            0);
        (await predicate(timeoutArgs)).Should().BeTrue();

        // 5. HTTP 429 TooManyRequests
        var tooManyRequestsArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)),
            0);
        (await predicate(tooManyRequestsArgs)).Should().BeTrue();

        // 6. HTTP 404 NotFound -> false
        var notFoundArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound)),
            0);
        (await predicate(notFoundArgs)).Should().BeFalse();

        // 7. HTTP 400 BadRequest -> false
        var badRequestArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)),
            0);
        (await predicate(badRequestArgs)).Should().BeFalse();

        // 8. HTTP 200 OK -> false
        var okArgs = new Polly.Retry.RetryPredicateArguments<System.Net.Http.HttpResponseMessage>(
            Polly.ResilienceContextPool.Shared.Get(),
            Polly.Outcome.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)),
            0);
        (await predicate(okArgs)).Should().BeFalse();
    }

    [Fact]
    public void AddWebhooks_CreatesNamedClientWithConfiguredPrimaryHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<Microsoft.Extensions.Http.HttpClientFactoryOptions>("EricksonLopez.Webhooks", o =>
        {
            o.HttpClientActions.Add(client => client.DefaultRequestHeaders.Add("X-Webhooks-Marker", "Configured"));
        });
        services.AddWebhooks();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IWebhookSender>();
        var clientField = typeof(WebhookSender).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var client = (System.Net.Http.HttpClient)clientField.GetValue(sender)!;

        client.DefaultRequestHeaders.Contains("X-Webhooks-Marker").Should().BeTrue();

        var optionsMonitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>();
        var factoryOptions = optionsMonitor.Get("EricksonLopez.Webhooks");
        factoryOptions.HttpMessageHandlerBuilderActions.Should().NotBeEmpty();

        var handlerFactory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var handler = handlerFactory.CreateHandler("EricksonLopez.Webhooks");
        var current = handler;
        while (current is DelegatingHandler dh)
        {
            current = dh.InnerHandler!;
        }
        var socketsHandler = current.Should().BeOfType<SocketsHttpHandler>().Subject;
        socketsHandler.AllowAutoRedirect.Should().BeFalse();
        socketsHandler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(15));
    }
}
