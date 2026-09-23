// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Configures options for receiving, authenticating, and validating inbound webhooks in ASP.NET Core.
/// </summary>
public sealed class WebhookReceiverOptions
{
    private string _secretKey = string.Empty;

    /// <summary>
    /// Gets or sets the primary shared secret key that verifies incoming HMAC-SHA256 signatures.
    /// </summary>
    /// <remarks>
    /// For zero-downtime key rotation across multiple active secrets, use <see cref="SecretKeys"/> or <see cref="AddSecret"/>.
    /// </remarks>
    public string SecretKey
    {
        get => _secretKey;
        set => _secretKey = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the collection of active secret keys that verify incoming signatures during rotation.
    /// </summary>
    public ImmutableList<string> SecretKeys { get; set; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Gets or sets the maximum allowable clock skew between sender and receiver.
    /// </summary>
    public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);

    private int _maxPayloadSizeBytes = 10 * 1024 * 1024; // 10 MB default

    /// <summary>
    /// Gets or sets the maximum allowable request body size in bytes to prevent excessive memory consumption.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to zero</exception>
    public int MaxPayloadSizeBytes
    {
        get => _maxPayloadSizeBytes;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MaxPayloadSizeBytes must be greater than zero.");
            }
            _maxPayloadSizeBytes = value;
        }
    }

    /// <summary>
    /// Gets or sets the URL route path prefix intercepted by the webhook receiver middleware.
    /// </summary>
    public string RoutePath { get; set; } = "/api/webhooks";

    /// <summary>
    /// Gets or sets the HTTP header protocol convention expected when extracting signature and timestamp information.
    /// </summary>
    public WebhookReceiverProtocol Protocol { get; set; } = WebhookReceiverProtocol.AutoDetect;

    /// <summary>
    /// Gets or sets a value indicating whether only HTTP POST requests should be intercepted and validated.
    /// Defaults to <c>false</c>, allowing non-POST requests to pass through to downstream handlers.
    /// </summary>
    public bool ValidatePostOnly { get; set; }

    /// <summary>
    /// Adds an active secret key to <see cref="SecretKeys"/> to support cryptographic key rotation.
    /// </summary>
    /// <param name="secretKey">The secret key to register for signature validation.</param>
    /// <returns>The current <see cref="WebhookReceiverOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="secretKey"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public WebhookReceiverOptions AddSecret(string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new ArgumentException("Secret key cannot be null or whitespace.", nameof(secretKey));
        }

        SecretKeys = SecretKeys.Add(secretKey);
        return this;
    }

    /// <summary>
    /// Enumerates all active secret keys configured for this receiver, combining <see cref="SecretKeys"/> and fallback <see cref="SecretKey"/>.
    /// </summary>
    /// <returns>An enumerable sequence of active secret keys.</returns>
    internal IEnumerable<string> GetActiveSecretKeys()
    {
        if (SecretKeys is not null)
        {
            var containsLegacy = false;
            foreach (var key in SecretKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    if (string.Equals(key, _secretKey, StringComparison.Ordinal))
                    {
                        containsLegacy = true;
                    }
                    yield return key;
                }
            }

            if (!containsLegacy && !string.IsNullOrWhiteSpace(_secretKey))
            {
                yield return _secretKey;
            }
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(_secretKey))
        {
            yield return _secretKey;
        }
    }
}

