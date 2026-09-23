// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Provides extension methods for configuring webhook receiver capabilities in ASP.NET Core applications.
/// </summary>
public static class WebhookAspNetCoreExtensions
{
    /// <summary>
    /// Registers the <see cref="IWebhookValidator"/> and receiver options into the service collection.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="configure">The delegate that configures <see cref="WebhookReceiverOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/></exception>
    public static IServiceCollection AddWebhookReceiver(
        this IServiceCollection services,
        Action<WebhookReceiverOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<WebhookReceiverOptions>()
            .Configure(configure)
            .Validate(o => o.MaxPayloadSizeBytes > 0, "MaxPayloadSizeBytes must be greater than zero.")
            .Validate(o => o.TimestampTolerance > TimeSpan.Zero, "TimestampTolerance must be greater than zero.")
            .Validate(o => !string.IsNullOrEmpty(o.RoutePath), "RoutePath must not be empty.")
            .ValidateOnStart();

        services.AddSingleton<IWebhookValidator, WebhookValidator>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="InMemoryWebhookReplayDetector"/> to protect against replay attacks in single-node environments.
    /// </summary>
    /// <remarks>
    /// This in-memory implementation is intended for development, testing, or single-node deployments.
    /// For multi-instance deployments, use a distributed replay detector such as Redis.
    /// </remarks>
    /// <param name="services">The service collection to register the detector into.</param>
    /// <param name="retentionPeriod">The duration to keep seen message IDs. Defaults to five minutes if omitted.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddInMemoryReplayDetector(
        this IServiceCollection services,
        TimeSpan? retentionPeriod = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWebhookReplayDetector>(sp => 
            retentionPeriod.HasValue 
                ? new InMemoryWebhookReplayDetector(retentionPeriod.Value) 
                : new InMemoryWebhookReplayDetector());

        return services;
    }

    /// <summary>
    /// Adds the <see cref="WebhookReceiverMiddleware"/> to the application request pipeline.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The original <see cref="IApplicationBuilder"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/></exception>
    public static IApplicationBuilder UseWebhookReceiver(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<WebhookReceiverMiddleware>();
    }

    /// <summary>
    /// Adds an endpoint filter to the route that verifies the authenticity of incoming webhooks via HMAC-SHA256 signature and timestamp validation.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <returns>The builder configured with the webhook endpoint filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static RouteHandlerBuilder RequireWebhookSignature(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<WebhookEndpointFilter>();
        return builder;
    }

    /// <summary>
    /// Adds an endpoint filter to the route group that verifies the authenticity of incoming webhooks via HMAC-SHA256 signature and timestamp validation.
    /// </summary>
    /// <param name="builder">The route group builder.</param>
    /// <returns>The builder configured with the webhook endpoint filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static RouteGroupBuilder RequireWebhookSignature(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<WebhookEndpointFilter>();
        return builder;
    }
}
