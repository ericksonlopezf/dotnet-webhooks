// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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

        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        byte[]? rentedPayload = null;
        Span<byte> payloadBytes = payloadByteCount <= 4096
            ? stackalloc byte[payloadByteCount]
            : (rentedPayload = ArrayPool<byte>.Shared.Rent(payloadByteCount));

        try
        {
            Encoding.UTF8.GetBytes(payload, payloadBytes);
            return ComputeSignature(secretKey, timestampSeconds, payloadBytes[..payloadByteCount], protocol, webhookId);
        }
        finally
        {
            if (rentedPayload is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedPayload);
            }
        }
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

        var prefixByteCount = Encoding.UTF8.GetByteCount(prefix);
        var totalContentLength = prefixByteCount + payloadBytes.Length;

        var keyByteCount = Encoding.UTF8.GetByteCount(secretKey);
        byte[]? rentedKey = null;
        byte[]? rentedContent = null;
        try
        {
            Span<byte> keyBytes = keyByteCount <= 256
                ? stackalloc byte[keyByteCount]
                : (rentedKey = ArrayPool<byte>.Shared.Rent(keyByteCount));

            Encoding.UTF8.GetBytes(secretKey, keyBytes);

            Span<byte> contentBytes = totalContentLength <= 4096
                ? stackalloc byte[totalContentLength]
                : (rentedContent = ArrayPool<byte>.Shared.Rent(totalContentLength));

            Encoding.UTF8.GetBytes(prefix, contentBytes);
            payloadBytes.CopyTo(contentBytes[prefixByteCount..totalContentLength]);

            Span<byte> hashBytes = stackalloc byte[32]; // SHA256 produces 32 bytes
            HMACSHA256.HashData(keyBytes[..keyByteCount], contentBytes[..totalContentLength], hashBytes);

            CryptographicOperations.ZeroMemory(keyBytes[..keyByteCount]);

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
        finally
        {
            if (rentedKey is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedKey);
            }

            if (rentedContent is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedContent);
            }
        }
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
                signatureSpan = signatureSpan[(spaceIndex + 1)..].TrimStart();
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

        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        byte[]? rentedPayload = null;
        Span<byte> payloadBytes = payloadByteCount <= 4096
            ? stackalloc byte[payloadByteCount]
            : (rentedPayload = ArrayPool<byte>.Shared.Rent(payloadByteCount));

        try
        {
            Encoding.UTF8.GetBytes(payload, payloadBytes);
            return VerifySignature(secretKey, timestampSeconds, payloadBytes[..payloadByteCount], receivedSignature, protocol, webhookId);
        }
        finally
        {
            if (rentedPayload is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedPayload);
            }
        }
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
        var maxExpected = Encoding.UTF8.GetMaxByteCount(expected.Length);
        var maxReceived = Encoding.UTF8.GetMaxByteCount(received.Length);
        var maxBytes = Math.Max(maxExpected, maxReceived);

        byte[]? rentedExpected = null;
        byte[]? rentedReceived = null;

        Span<byte> expectedBytes = maxBytes <= 256
            ? stackalloc byte[maxBytes]
            : (rentedExpected = ArrayPool<byte>.Shared.Rent(maxBytes));

        Span<byte> receivedBytes = maxBytes <= 256
            ? stackalloc byte[maxBytes]
            : (rentedReceived = ArrayPool<byte>.Shared.Rent(maxBytes));

        expectedBytes.Clear();
        receivedBytes.Clear();

        try
        {
            var expectedLen = Encoding.UTF8.GetBytes(expected, expectedBytes);
            var receivedLen = Encoding.UTF8.GetBytes(received, receivedBytes);

            bool lengthsMatch = expectedLen == receivedLen;
            bool contentsMatch = CryptographicOperations.FixedTimeEquals(expectedBytes[..expectedLen], receivedBytes[..expectedLen]);

            return lengthsMatch & contentsMatch;
        }
        finally
        {
            if (rentedExpected is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedExpected);
            }

            if (rentedReceived is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedReceived);
            }
        }
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

        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(8192);
        long totalBytesRead = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await payloadStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
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
            ArrayPool<byte>.Shared.Return(rentedBuffer);
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
        VerifySignatureAsync(secretKey is null ? Array.Empty<string>() : new[] { secretKey }, timestampSeconds, payloadStream, receivedSignature, protocol, webhookId, maxPayloadBytes: null, cancellationToken);

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
        VerifySignatureAsync(secretKey is null ? Array.Empty<string>() : new[] { secretKey }, timestampSeconds, payloadStream, receivedSignature, protocol, webhookId, maxPayloadBytes, cancellationToken);

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
        if (signatureSpan.IsEmpty)
        {
            return false;
        }

        var candidates = new List<string>(5);
        while (!signatureSpan.IsEmpty)
        {
            var spaceIndex = signatureSpan.IndexOf(' ');
            ReadOnlySpan<char> candidate;
            if (spaceIndex >= 0)
            {
                candidate = signatureSpan[..spaceIndex];
                signatureSpan = signatureSpan[(spaceIndex + 1)..].TrimStart();
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

        if (candidates.Count == 0)
        {
            return false;
        }

        var needsStandard = false;
        var needsErickson = false;

        foreach (var cand in candidates)
        {
            if (protocol == WebhookReceiverProtocol.Standard || (protocol == WebhookReceiverProtocol.AutoDetect && cand.StartsWith("v1,", StringComparison.Ordinal)))
            {
                if (!string.IsNullOrWhiteSpace(webhookId))
                {
                    needsStandard = true;
                }
            }

            if (protocol == WebhookReceiverProtocol.EricksonLopez || (protocol == WebhookReceiverProtocol.AutoDetect && cand.StartsWith("v1=", StringComparison.Ordinal)))
            {
                needsErickson = true;
            }
        }

        if (!needsStandard && !needsErickson)
        {
            if (protocol == WebhookReceiverProtocol.AutoDetect)
            {
                needsErickson = true;
            }
            else
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

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(8192);
            long totalBytesRead = 0;
            try
            {
                int bytesRead;
                while ((bytesRead = await payloadStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
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
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }

            foreach (var hmac in standardHashes)
            {
                var computed = FormatSignature(hmac, isStandard: true);
                foreach (var cand in candidates)
                {
                    if (computed is not null && (cand.StartsWith("v1,", StringComparison.Ordinal) || protocol == WebhookReceiverProtocol.Standard))
                    {
                        if (ConstantTimeEquals(computed.AsSpan(), cand.AsSpan()))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (var hmac in ericksonHashes)
            {
                var computed = FormatSignature(hmac, isStandard: false);
                foreach (var cand in candidates)
                {
                    if (computed is not null && (cand.StartsWith("v1=", StringComparison.Ordinal) || protocol == WebhookReceiverProtocol.EricksonLopez || protocol == WebhookReceiverProtocol.AutoDetect))
                    {
                        if (ConstantTimeEquals(computed.AsSpan(), cand.AsSpan()))
                        {
                            return true;
                        }
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
        var keyByteCount = Encoding.UTF8.GetByteCount(secretKey);
        byte[]? rentedKey = null;
        byte[]? rentedPrefix = null;

        try
        {
            Span<byte> keyBytes = keyByteCount <= 256
                ? stackalloc byte[keyByteCount]
                : (rentedKey = ArrayPool<byte>.Shared.Rent(keyByteCount));

            Encoding.UTF8.GetBytes(secretKey, keyBytes);

            var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keyBytes[..keyByteCount]);
            CryptographicOperations.ZeroMemory(keyBytes[..keyByteCount]);

            var prefixByteCount = Encoding.UTF8.GetByteCount(prefix);
            Span<byte> prefixBytes = prefixByteCount <= 256
                ? stackalloc byte[prefixByteCount]
                : (rentedPrefix = ArrayPool<byte>.Shared.Rent(prefixByteCount));

            Encoding.UTF8.GetBytes(prefix, prefixBytes);
            hmac.AppendData(prefixBytes[..prefixByteCount]);

            return hmac;
        }
        finally
        {
            if (rentedKey is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedKey);
            }
            if (rentedPrefix is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedPrefix);
            }
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
