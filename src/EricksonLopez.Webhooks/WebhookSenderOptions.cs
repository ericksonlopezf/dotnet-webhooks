// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Configures the behavior and security constraints for outbound webhook delivery.
/// </summary>
public sealed class WebhookSenderOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private int _maxPayloadSizeBytes = 10 * 1024 * 1024; // 10 MB default

    /// <summary>
    /// Gets or sets the HTTP request timeout for each delivery attempt.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to <see cref="TimeSpan.Zero"/></exception>
    public TimeSpan Timeout
    {
        get => _timeout;
        set => _timeout = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be greater than zero.");
    }

    /// <summary>
    /// Gets or sets the maximum allowable request payload size in bytes to prevent excessive memory consumption.
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
    /// Gets or sets a value indicating whether strict SSRF mitigation and private IP blocking are enabled.
    /// </summary>
    public bool EnableSsrfProtection { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether connections to private or loopback network addresses are permitted.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unencrypted HTTP connections are explicitly permitted.
    /// </summary>
    public bool DangerousAllowInsecureHttp
    {
        get => !SsrfProtection.RequireHttps;
        set => SsrfProtection.RequireHttps = !value;
    }

    /// <summary>
    /// Gets the underlying SSRF protection options configuring DNS validation and IP filtering.
    /// </summary>
    public EricksonLopez.Security.Network.SsrfProtectionOptions SsrfProtection { get; } = new()
    {
        RequireHttps = true // Secure by default
    };

    private WebhookSenderProtocol _protocol = WebhookSenderProtocol.EricksonLopez;

    /// <summary>
    /// Gets or sets the HTTP header protocol convention used when dispatching webhooks.
    /// </summary>
    public WebhookSenderProtocol Protocol
    {
        get => _protocol;
        set => _protocol = value;
    }
}
