// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Encapsulates a cryptographic webhook secret while preventing accidental leakage in logs and telemetry.
/// </summary>
/// <remarks>
/// This struct overrides <see cref="ToString"/> to return a redacted placeholder.
/// </remarks>
[DebuggerDisplay("{ToString()}")]
public readonly record struct WebhookSecret
{
    private readonly string _secret;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSecret"/> struct with the specified secret string.
    /// </summary>
    /// <param name="secret">The raw secret string.</param>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public WebhookSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret cannot be null or whitespace.", nameof(secret));
        }

        _secret = secret;
    }

    /// <summary>
    /// Retrieves the underlying secret as an unredacted string.
    /// </summary>
    /// <returns>The unredacted secret string.</returns>
    public string GetUnsecuredString() => _secret;

    /// <inheritdoc/>
    public override string ToString() => "[REDACTED]";

    /// <summary>
    /// Converts a string to a <see cref="WebhookSecret"/> instance.
    /// </summary>
    /// <param name="secret">The raw secret string to convert.</param>
    /// <returns>A new <see cref="WebhookSecret"/> instance wrapping the specified string.</returns>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static implicit operator WebhookSecret(string secret) => new(secret);

    /// <summary>
    /// Converts a <see cref="WebhookSecret"/> instance to its underlying secret string.
    /// </summary>
    /// <param name="secret">The <see cref="WebhookSecret"/> instance to convert.</param>
    /// <returns>The underlying secret string.</returns>
    public static implicit operator string(WebhookSecret secret) => secret._secret;
}
