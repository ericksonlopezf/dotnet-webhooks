// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides HMAC-SHA256 signature generation and constant-time verification for outbound and inbound webhooks.
/// </summary>
/// <remarks>
/// All verification routines perform constant-time comparisons to mitigate timing attacks.
/// Memory-sensitive cryptographic keys are wiped after hashing.
/// </remarks>
public static class WebhookSigner
{
    private const string EricksonLopezSignaturePrefix = "v1=";
    private const string StandardSignaturePrefix = "v1,";

    private static readonly AsyncLocal<Action<IncrementalHash>?> _hashCreatedHook = new();
    internal static Action<IncrementalHash>? OnHashCreated
    {
        get => _hashCreatedHook.Value;
        set => _hashCreatedHook.Value = value;
    }

    private static readonly AsyncLocal<Action<byte[]>?> _keyBytesZeroedHook = new();
    internal static Action<byte[]>? OnKeyBytesZeroed
    {
        get => _keyBytesZeroedHook.Value;
        set => _keyBytesZeroedHook.Value = value;
    }

    private static readonly AsyncLocal<ArrayPool<byte>?> _bufferPoolHook = new();
    internal static ArrayPool<byte> BufferPool
    {
        get => _bufferPoolHook.Value ?? ArrayPool<byte>.Shared;
        set => _bufferPoolHook.Value = value;
    }

    private static readonly AsyncLocal<Action<byte[]>?> _bufferReturnedHook = new();
    internal static Action<byte[]>? OnBufferReturned
    {
        get => _bufferReturnedHook.Value;
        set => _bufferReturnedHook.Value = value;
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a string payload using the default EricksonLopez protocol.
    /// </summary>
    /// <param name="secretKey">The shared secret key that generates the signature.</param>
    /// <param name="timestampSeconds">The UNIX timestamp in seconds at which the signature was generated.</param>
    /// <param name="payload">The UTF-8 string payload content to sign.</param>
    /// <returns>The formatted signature string.</returns>
    public static string ComputeSignature(string secretKey, long timestampSeconds, string payload) =>
        ComputeSignature(secretKey, timestampSeconds, payload, WebhookSenderProtocol.EricksonLopez, null);

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a string payload according to the specified protocol.
    /// </summary>
    /// <param name="secretKey">The shared secret key that generates the signature.</param>
    /// <param name="timestampSeconds">The UNIX timestamp in seconds at which the signature was generated.</param>
    /// <param name="payload">The UTF-8 string payload content to sign.</param>
    /// <param name="protocol">The webhook protocol specification determining the signature format and canonicalization.</param>
    /// <param name="webhookId">The unique message or delivery identifier. Required when <paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/>.</param>
    /// <returns>The formatted signature string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="secretKey"/> or <paramref name="payload"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/> and <paramref name="webhookId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static string ComputeSignature(
        string secretKey,
        long timestampSeconds,
        string payload,
        WebhookSenderProtocol protocol,
        string? webhookId = null)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(payload);

        var isStandard = protocol == WebhookSenderProtocol.Standard;
        if (isStandard && string.IsNullOrWhiteSpace(webhookId))
        {
            throw new ArgumentException("StandardWebhooks protocol requires a non-empty webhookId (msg_id).", nameof(webhookId));
        }

        var prefix = isStandard
            ? $"{webhookId}.{timestampSeconds}."
            : $"{timestampSeconds}.";

        using var hmac = CreateHmac(secretKey, prefix);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        hmac.AppendData(payloadBytes);
        return FormatSignature(hmac, isStandard);
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for raw webhook payload bytes according to the specified protocol.
    /// </summary>
    /// <param name="secretKey">The shared secret key that generates the signature.</param>
    /// <param name="timestampSeconds">The UNIX timestamp in seconds at which the signature was generated.</param>
    /// <param name="payloadBytes">The raw payload byte span.</param>
    /// <param name="protocol">The webhook protocol specification determining the signature format and canonicalization.</param>
    /// <param name="webhookId">The unique message or delivery identifier. Required when <paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/>.</param>
    /// <returns>The formatted signature string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="secretKey"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/> and <paramref name="webhookId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static string ComputeSignature(
        string secretKey,
        long timestampSeconds,
        ReadOnlySpan<byte> payloadBytes,
        WebhookSenderProtocol protocol,
        string? webhookId = null)
    {
        ArgumentNullException.ThrowIfNull(secretKey);

        var isStandard = protocol == WebhookSenderProtocol.Standard;
        if (isStandard && string.IsNullOrWhiteSpace(webhookId))
        {
            throw new ArgumentException("StandardWebhooks protocol requires a non-empty webhookId (msg_id).", nameof(webhookId));
        }

        var prefix = isStandard
            ? $"{webhookId}.{timestampSeconds}."
            : $"{timestampSeconds}.";

        using var hmac = CreateHmac(secretKey, prefix);
        hmac.AppendData(payloadBytes);
        return FormatSignature(hmac, isStandard);
    }

    /// <summary>
    /// Verifies the signature of an incoming string payload using constant-time comparison with auto-detected protocol.
    /// </summary>
    /// <param name="secretKey">The shared secret key to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payload">The raw string payload to verify.</param>
    /// <param name="receivedSignature">The signature header value received from the request.</param>
    /// <returns><see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(string secretKey, long timestampSeconds, string payload, string receivedSignature) =>
        VerifySignature(secretKey, timestampSeconds, payload, receivedSignature, WebhookReceiverProtocol.AutoDetect, null);


    /// <summary>
    /// Verifies the signature of raw incoming webhook payload bytes using constant-time comparison according to protocol rules.
    /// </summary>
    /// <param name="secretKey">The shared secret key to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payloadBytes">The raw request payload byte span.</param>
    /// <param name="receivedSignature">The signature header value received, which may contain multiple space-delimited signatures.</param>
    /// <param name="protocol">The expected webhook protocol convention.</param>
    /// <param name="webhookId">The unique message or delivery identifier, if available.</param>
    /// <returns><see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(
        string secretKey,
        long timestampSeconds,
        ReadOnlySpan<byte> payloadBytes,
        string receivedSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId = null)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(receivedSignature))
        {
            return false;
        }

        // StandardWebhooks allows space-separated signatures: "v1,signature1 v1,signature2"
        var signatureSpan = receivedSignature.AsSpan().Trim();
        var candidateCount = 0;

        while (!signatureSpan.IsEmpty)
        {
            var spaceIndex = signatureSpan.IndexOf(' ');
            ReadOnlySpan<char> candidate;
            if (spaceIndex >= 0)
            {
                candidate = signatureSpan[..spaceIndex];
                signatureSpan = signatureSpan[(spaceIndex + 1)..];
            }
            else
            {
                candidate = signatureSpan;
                signatureSpan = default;
            }

            if (candidate.IsEmpty)
            {
                continue;
            }

            if (++candidateCount > 5)
            {
                return false;
            }

            if (CheckCandidateSignature(secretKey, timestampSeconds, payloadBytes, candidate, protocol, webhookId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verifies the signature of an incoming string payload using constant-time comparison according to protocol rules.
    /// </summary>
    /// <param name="secretKey">The shared secret key to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payload">The raw request payload string to verify.</param>
    /// <param name="receivedSignature">The signature header value received, which may contain multiple space-delimited signatures.</param>
    /// <param name="protocol">The expected webhook protocol convention.</param>
    /// <param name="webhookId">The unique message or delivery identifier, if available.</param>
    /// <returns><see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(
        string secretKey,
        long timestampSeconds,
        string payload,
        string receivedSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId = null)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || payload is null || string.IsNullOrWhiteSpace(receivedSignature))
        {
            return false;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        return VerifySignature(secretKey, timestampSeconds, payloadBytes, receivedSignature, protocol, webhookId);
    }

    private static bool CheckCandidateSignature(
        string secretKey,
        long timestampSeconds,
        ReadOnlySpan<byte> payloadBytes,
        ReadOnlySpan<char> candidateSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId)
    {
        if (protocol == WebhookReceiverProtocol.Standard || (protocol == WebhookReceiverProtocol.AutoDetect && candidateSignature.StartsWith("v1,", StringComparison.Ordinal)))
        {
            if (string.IsNullOrWhiteSpace(webhookId))
            {
                return false;
            }

            var expectedStandard = ComputeSignature(secretKey, timestampSeconds, payloadBytes, WebhookSenderProtocol.Standard, webhookId);
            if (ConstantTimeEquals(expectedStandard.AsSpan(), candidateSignature))
            {
                return true;
            }
        }

        if (protocol == WebhookReceiverProtocol.EricksonLopez || protocol == WebhookReceiverProtocol.AutoDetect)
        {
            var expectedErickson = ComputeSignature(secretKey, timestampSeconds, payloadBytes, WebhookSenderProtocol.EricksonLopez, null);
            if (ConstantTimeEquals(expectedErickson.AsSpan(), candidateSignature))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstantTimeEquals(ReadOnlySpan<char> expected, ReadOnlySpan<char> received)
    {
        if (expected.Length != received.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            MemoryMarshal.AsBytes(expected),
            MemoryMarshal.AsBytes(received));
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a payload stream at a specific timestamp according to the specified protocol.
    /// </summary>
    /// <remarks>
    /// Employs incremental hashing over rented buffers to avoid large heap allocations and LOH pressure.
    /// </remarks>
    /// <param name="secretKey">The shared secret key that generates the signature.</param>
    /// <param name="timestampSeconds">The UNIX timestamp in seconds at which the signature was generated.</param>
    /// <param name="payloadStream">The readable payload stream to hash incrementally.</param>
    /// <param name="protocol">The webhook protocol specification determining the signature format.</param>
    /// <param name="webhookId">The unique message or delivery identifier. Required when <paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/>.</param>
    /// <param name="maxPayloadBytes">The optional maximum allowable bytes to read from the stream before aborting.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the formatted signature string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="secretKey"/> or <paramref name="payloadStream"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="protocol"/> is <see cref="WebhookSenderProtocol.Standard"/> and <paramref name="webhookId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="WebhookPayloadTooLargeException">The payload stream exceeds <paramref name="maxPayloadBytes"/></exception>
    public static async Task<string> ComputeSignatureAsync(
        string secretKey,
        long timestampSeconds,
        Stream payloadStream,
        WebhookSenderProtocol protocol,
        string? webhookId = null,
        long? maxPayloadBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(payloadStream);

        var isStandard = protocol == WebhookSenderProtocol.Standard;
        if (isStandard && string.IsNullOrWhiteSpace(webhookId))
        {
            throw new ArgumentException("StandardWebhooks protocol requires a non-empty webhookId (msg_id).", nameof(webhookId));
        }

        var prefix = isStandard
            ? $"{webhookId}.{timestampSeconds}."
            : $"{timestampSeconds}.";

        using var hmac = CreateHmac(secretKey, prefix);

        byte[] rentedBuffer = BufferPool.Rent(8192);
        long totalBytesRead = 0;
        try
        {
            while (true)
            {
                int bytesRead = await payloadStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
                if (maxPayloadBytes.HasValue && totalBytesRead > maxPayloadBytes.Value)
                {
                    throw new WebhookPayloadTooLargeException(maxPayloadBytes.Value);
                }

                hmac.AppendData(rentedBuffer, 0, bytesRead);
            }
        }
        finally
        {
            BufferPool.Return(rentedBuffer);
            OnBufferReturned?.Invoke(rentedBuffer);
        }

        return FormatSignature(hmac, isStandard);
    }

    /// <summary>
    /// Verifies the signature of an incoming webhook payload stream using constant-time comparison according to protocol rules.
    /// </summary>
    /// <remarks>
    /// Employs incremental hashing over rented buffers to avoid large heap allocations and LOH pressure.
    /// </remarks>
    /// <param name="secretKey">The shared secret key to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payloadStream">The readable payload stream to verify.</param>
    /// <param name="receivedSignature">The signature header value received, which may contain multiple space-delimited signatures.</param>
    /// <param name="protocol">The expected webhook receiver protocol convention.</param>
    /// <param name="webhookId">The unique message or delivery identifier, if available.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains <see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
    public static Task<bool> VerifySignatureAsync(
        string secretKey,
        long timestampSeconds,
        Stream payloadStream,
        string receivedSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId = null,
        CancellationToken cancellationToken = default) =>
        VerifySignatureAsync(new[] { secretKey }, timestampSeconds, payloadStream, receivedSignature, protocol, webhookId, maxPayloadBytes: null, cancellationToken);

    /// <summary>
    /// Verifies the signature of an incoming webhook payload stream using constant-time comparison according to protocol rules,
    /// aborting early if the stream exceeds <paramref name="maxPayloadBytes"/>.
    /// </summary>
    /// <param name="secretKey">The shared secret key to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payloadStream">The readable payload stream to verify.</param>
    /// <param name="receivedSignature">The signature header value received, which may contain multiple space-delimited signatures.</param>
    /// <param name="protocol">The expected webhook receiver protocol convention.</param>
    /// <param name="webhookId">The unique message or delivery identifier, if available.</param>
    /// <param name="maxPayloadBytes">The optional maximum allowable bytes to read from the stream before aborting.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains <see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="WebhookPayloadTooLargeException">The payload stream exceeds <paramref name="maxPayloadBytes"/></exception>
    public static Task<bool> VerifySignatureAsync(
        string secretKey,
        long timestampSeconds,
        Stream payloadStream,
        string receivedSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId,
        long? maxPayloadBytes,
        CancellationToken cancellationToken = default) =>
        VerifySignatureAsync(new[] { secretKey }, timestampSeconds, payloadStream, receivedSignature, protocol, webhookId, maxPayloadBytes, cancellationToken);

    /// <summary>
    /// Verifies the signature of an incoming webhook payload stream against multiple secret keys using constant-time comparison,
    /// aborting early if the stream exceeds <paramref name="maxPayloadBytes"/>.
    /// </summary>
    /// <remarks>
    /// Hashing for all keys is performed in a single pass over the stream.
    /// </remarks>
    /// <param name="secretKeys">The collection of shared secret keys to verify against the signature.</param>
    /// <param name="timestampSeconds">The timestamp header value in seconds.</param>
    /// <param name="payloadStream">The readable payload stream to verify.</param>
    /// <param name="receivedSignature">The signature header value received, which may contain multiple space-delimited signatures.</param>
    /// <param name="protocol">The expected webhook receiver protocol convention.</param>
    /// <param name="webhookId">The unique message or delivery identifier, if available.</param>
    /// <param name="maxPayloadBytes">The optional maximum allowable bytes to read from the stream before aborting.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains <see langword="true"/> if the signature is valid for any key; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="WebhookPayloadTooLargeException">The payload stream exceeds <paramref name="maxPayloadBytes"/></exception>
    public static async Task<bool> VerifySignatureAsync(
        IEnumerable<string> secretKeys,
        long timestampSeconds,
        Stream payloadStream,
        string receivedSignature,
        WebhookReceiverProtocol protocol,
        string? webhookId,
        long? maxPayloadBytes,
        CancellationToken cancellationToken = default)
    {
        if (secretKeys is null || payloadStream is null || string.IsNullOrWhiteSpace(receivedSignature))
        {
            return false;
        }

        var signatureSpan = receivedSignature.AsSpan().Trim();
        var candidates = new List<string>(5);
        while (!signatureSpan.IsEmpty)
        {
            var spaceIndex = signatureSpan.IndexOf(' ');
            ReadOnlySpan<char> candidate;
            if (spaceIndex >= 0)
            {
                candidate = signatureSpan[..spaceIndex];
                signatureSpan = signatureSpan[(spaceIndex + 1)..];
            }
            else
            {
                candidate = signatureSpan;
                signatureSpan = default;
            }

            if (!candidate.IsEmpty)
            {
                candidates.Add(candidate.ToString());
                if (candidates.Count > 5)
                {
                    return false;
                }
            }
        }

        var needsStandard = false;
        var needsErickson = false;

        if (protocol == WebhookReceiverProtocol.Standard)
        {
            if (string.IsNullOrWhiteSpace(webhookId))
            {
                return false;
            }
            needsStandard = true;
        }
        else if (protocol == WebhookReceiverProtocol.EricksonLopez)
        {
            needsErickson = true;
        }
        else
        {
            foreach (var cand in candidates)
            {
                if (cand.StartsWith("v1,", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(webhookId))
                    {
                        needsStandard = true;
                    }
                }
                else if (cand.StartsWith("v1=", StringComparison.Ordinal))
                {
                    needsErickson = true;
                }
            }

            if (!needsStandard && !needsErickson)
            {
                return false;
            }
        }

        var standardHashes = new List<IncrementalHash>();
        var ericksonHashes = new List<IncrementalHash>();
        var allHashes = new List<IncrementalHash>();

        try
        {
            foreach (var secretKey in secretKeys)
            {
                if (string.IsNullOrWhiteSpace(secretKey)) continue;

                if (needsStandard)
                {
                    var hmac = CreateHmac(secretKey, $"{webhookId}.{timestampSeconds}.");
                    standardHashes.Add(hmac);
                    allHashes.Add(hmac);
                }

                if (needsErickson)
                {
                    var hmac = CreateHmac(secretKey, $"{timestampSeconds}.");
                    ericksonHashes.Add(hmac);
                    allHashes.Add(hmac);
                }
            }

            if (allHashes.Count == 0)
            {
                return false;
            }

            byte[] rentedBuffer = BufferPool.Rent(8192);
            long totalBytesRead = 0;
            try
            {
                while (true)
                {
                    int bytesRead = await payloadStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalBytesRead += bytesRead;
                    if (maxPayloadBytes.HasValue && totalBytesRead > maxPayloadBytes.Value)
                    {
                        throw new WebhookPayloadTooLargeException(maxPayloadBytes.Value);
                    }

                    foreach (var hmac in allHashes)
                    {
                        hmac.AppendData(rentedBuffer, 0, bytesRead);
                    }
                }
            }
            finally
            {
                BufferPool.Return(rentedBuffer);
                OnBufferReturned?.Invoke(rentedBuffer);
            }

            foreach (var hmac in standardHashes)
            {
                var computed = FormatSignature(hmac, isStandard: true);
                foreach (var cand in candidates)
                {
                    if (ConstantTimeEquals(computed.AsSpan(), cand.AsSpan()))
                    {
                        return true;
                    }
                }
            }

            foreach (var hmac in ericksonHashes)
            {
                var computed = FormatSignature(hmac, isStandard: false);
                foreach (var cand in candidates)
                {
                    if (ConstantTimeEquals(computed.AsSpan(), cand.AsSpan()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            foreach (var hmac in allHashes)
            {
                hmac.Dispose();
            }
        }
    }

    private static IncrementalHash CreateHmac(string secretKey, string prefix)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        try
        {
            var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keyBytes);
            var prefixBytes = Encoding.UTF8.GetBytes(prefix);
            hmac.AppendData(prefixBytes);
            OnHashCreated?.Invoke(hmac);
            return hmac;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            OnKeyBytesZeroed?.Invoke(keyBytes);
        }
    }

    private static string FormatSignature(IncrementalHash hmac, bool isStandard)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        hmac.GetHashAndReset(hashBytes);

        if (isStandard)
        {
            var base64 = Convert.ToBase64String(hashBytes);
            return $"{StandardSignaturePrefix}{base64}";
        }

#if NET9_0_OR_GREATER
        var hexHash = Convert.ToHexStringLower(hashBytes);
#else
        var hexHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
#endif
        return $"{EricksonLopezSignaturePrefix}{hexHash}";
    }
}
