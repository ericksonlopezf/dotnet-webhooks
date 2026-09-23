// Copyright © Erickson Lopez. MIT License.
using System;
using System.Net.Http;
using EricksonLopez.Security.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides extension methods for registering webhook services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class WebhookServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IWebhookSender"/> and supporting infrastructure into the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="configure">An optional action to configure the <see cref="WebhookSenderOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddWebhooks(
        this IServiceCollection services,
        Action<WebhookSenderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<WebhookSenderOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder
            .Validate(o => o.Timeout > TimeSpan.Zero, "Timeout must be greater than zero.")
            .Validate(o => o.MaxPayloadSizeBytes > 0, "MaxPayloadSizeBytes must be greater than zero.")
            .ValidateOnStart();

        services.AddTransient(sp => sp.GetRequiredService<IOptions<WebhookSenderOptions>>().Value);

        services.TryAddSingleton<IWebhookDeadLetterSink, NoOpWebhookDeadLetterSink>();

        services.AddHttpClient("EricksonLopez.Webhooks")
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WebhookSenderOptions>>().Value;
                return SafeSocketsHttpHandlerFactory.Create(options);
            })
            .AddStandardResilienceHandler(options =>
            {
                // Basic resilient defaults for webhooks.
                options.Retry.ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null || 
                    (args.Outcome.Result?.StatusCode is >= System.Net.HttpStatusCode.InternalServerError 
                        or System.Net.HttpStatusCode.RequestTimeout 
                        or System.Net.HttpStatusCode.TooManyRequests));
            });

        services.AddTransient<IWebhookSender>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("EricksonLopez.Webhooks");
            var options = sp.GetRequiredService<IOptions<WebhookSenderOptions>>().Value;
            var dlq = sp.GetService<IWebhookDeadLetterSink>();
            var logger = sp.GetService<ILogger<WebhookSender>>();
            
            return new WebhookSender(client, dlq, options, logger);
        });

        return services;
    }
}
