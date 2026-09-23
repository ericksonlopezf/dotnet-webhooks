// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Security.Network;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides factory methods for creating <see cref="SocketsHttpHandler"/> instances with DNS rebinding SSRF protection.
/// </summary>
internal static class SafeSocketsHttpHandlerFactory
{
    private static readonly AsyncLocal<Action<Socket>?> _socketCreatedHook = new();
    internal static Action<Socket>? OnSocketCreated
    {
        get => _socketCreatedHook.Value;
        set => _socketCreatedHook.Value = value;
    }

    private static readonly AsyncLocal<Func<string, CancellationToken, Task<IPAddress[]>>?> _dnsResolverHook = new();
    internal static Func<string, CancellationToken, Task<IPAddress[]>>? DnsResolver
    {
        get => _dnsResolverHook.Value;
        set => _dnsResolverHook.Value = value;
    }

    /// <summary>
    /// Creates a configured <see cref="SocketsHttpHandler"/> instance based on the specified options.
    /// </summary>
    /// <param name="options">The webhook sender options configuring SSRF protection and network constraints.</param>
    /// <returns>A configured <see cref="SocketsHttpHandler"/> instance.</returns>
    public static SocketsHttpHandler Create(WebhookSenderOptions options)
    {
        if (!options.EnableSsrfProtection)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            };
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            // Resolve the hostname right before connecting the socket to prevent TOCTOU DNS rebinding.
            var addresses = DnsResolver is not null
                ? await DnsResolver(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false)
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var targetIp = addresses[0];

            if (!options.AllowPrivateNetworks && IsPrivateOrLoopback(targetIp))
            {
                throw new SecurityException($"DNS Rebinding SSRF detected: The hostname '{context.DnsEndPoint.Host}' resolved to a private/loopback IP address ({targetIp}).");
            }

            var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            OnSocketCreated?.Invoke(socket);

            try
            {
                await socket.ConnectAsync(new IPEndPoint(targetIp, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

        return handler;
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 0 || // 0.0.0.0/8
                   bytes[0] == 10 || // 10.0.0.0/8
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16.0.0/12
                   (bytes[0] == 192 && bytes[1] == 168) || // 192.168.0.0/16
                   (bytes[0] == 169 && bytes[1] == 254); // 169.254.0.0/16 (Link-local, AWS/GCP Metadata)
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            // fd00::/8
            if (bytes[0] == 0xfd)
            {
                return true;
            }
            // fc00::/7
            if (bytes[0] == 0xfc)
            {
                return true;
            }
        }

        return false;
    }
}
