// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Webhooks;
using EricksonLopez.Webhooks.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring Redis-backed webhook services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class RedisWebhookServiceCollectionExtensions
{
    /// <summary>
    /// Registers a distributed, Redis-backed webhook replay detector into the service collection.
    /// </summary>
    /// <remarks>
    /// Requires an <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
    /// </remarks>
    /// <param name="services">The service collection to register the detector into.</param>
    /// <param name="retentionPeriod">The duration for which processed message identifiers are retained in Redis. Defaults to ten minutes if omitted.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddRedisWebhookReplayDetector(this IServiceCollection services, TimeSpan? retentionPeriod = null)
    {
        var retention = retentionPeriod ?? TimeSpan.FromMinutes(10);

        services.TryAddSingleton<IWebhookReplayDetector>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisWebhookReplayDetector(multiplexer, retention);
        });

        return services;
    }

    /// <summary>
    /// Registers a distributed, Redis-backed dead-letter queue sink into the service collection.
    /// </summary>
    /// <remarks>
    /// Requires an <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
    /// </remarks>
    /// <param name="services">The service collection to register the sink into.</param>
    /// <param name="listKey">The Redis list key name where failed payloads are appended. Defaults to <c>"webhook:dlq"</c>.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddRedisWebhookDeadLetterSink(this IServiceCollection services, string listKey = "webhook:dlq")
    {
        services.TryAddSingleton<IWebhookDeadLetterSink>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisWebhookDeadLetterSink(multiplexer, listKey);
        });

        return services;
    }
}
