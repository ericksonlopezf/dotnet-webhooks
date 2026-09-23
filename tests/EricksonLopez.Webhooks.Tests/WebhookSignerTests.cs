// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class WebhookSignerTests
{
    private const string Secret = "whsec_super_secret_test_key_12345";
    private const long DefaultTimestamp = 1700000000L;

    [Fact]
    public void ComputeSignature_ValidInputs_GeneratesDeterministicHexSignature()
    {
        var payload = "{\"event\":\"invoice.created\",\"amount\":150.00}";

        var sig1 = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        var sig2 = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        sig1.Should().StartWith("v1=");
        sig1.Length.Should().Be(67); // "v1=" (3 chars) + 64 hex chars (32 bytes SHA-256)
        sig1.Should().Be(sig2);
    }

    [Fact]
    public void ComputeSignature_NullSecretKey_ThrowsArgumentNullException()
    {
        var act = () => WebhookSigner.ComputeSignature(null!, DefaultTimestamp, "{}");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("secretKey");
    }

    [Fact]
    public void ComputeSignature_SpanOverload_NullSecretKey_ThrowsArgumentNullException()
    {
        var act = () => WebhookSigner.ComputeSignature(null!, DefaultTimestamp, ReadOnlySpan<byte>.Empty, WebhookSenderProtocol.EricksonLopez);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("secretKey");
    }

    [Fact]
    public void ComputeSignature_NullPayload_ThrowsArgumentNullException()
    {
        var act = () => WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("payload");
    }

    [Fact]
    public void ComputeSignature_EmptySecretKeyAndPayload_ComputesValidHmac()
    {
        var sig = WebhookSigner.ComputeSignature(string.Empty, DefaultTimestamp, string.Empty);

        sig.Should().StartWith("v1=");
        sig.Length.Should().Be(67);
    }

    [Fact]
    public void ComputeSignature_Utf8SpecialCharacters_ProducesDeterministicDigest()
    {
        var payload = "{\"greeting\":\"¡Hola, mundo! \ud83d\ude80\",\"author\":\"José Pérez\"}";

        var sig1 = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        var sig2 = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        sig1.Should().Be(sig2);
        sig1.Should().StartWith("v1=");
    }

    [Fact]
    public void ComputeSignature_DifferentTimestamps_ProducesDifferentSignatures()
    {
        var payload = "{\"event\":\"test\"}";

        var sig1 = WebhookSigner.ComputeSignature(Secret, 1700000000L, payload);
        var sig2 = WebhookSigner.ComputeSignature(Secret, 1700000001L, payload);

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void VerifySignature_MatchingPayloadAndKey_ReturnsTrue()
    {
        var payload = "{\"event\":\"invoice.paid\",\"invoiceId\":\"inv-999\"}";
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, signature);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_TamperedPayload_ReturnsFalse()
    {
        var originalPayload = "{\"amount\":100}";
        var tamperedPayload = "{\"amount\":999}";
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, originalPayload);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, tamperedPayload, signature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_MismatchedSecretKey_ReturnsFalse()
    {
        var payload = "{\"event\":\"ping\"}";
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        var isValid = WebhookSigner.VerifySignature("whsec_wrong_key", DefaultTimestamp, payload, signature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_MismatchedTimestamp_ReturnsFalse()
    {
        var payload = "{\"event\":\"status.updated\"}";
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp + 100, payload, signature);

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void VerifySignature_NullOrWhitespaceSecretKey_ReturnsFalse(string? secretKey)
    {
        var payload = "{\"data\":\"test\"}";
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        var isValid = WebhookSigner.VerifySignature(secretKey!, DefaultTimestamp, payload, signature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_NullPayload_ReturnsFalse()
    {
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}");

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, null!, signature);

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void VerifySignature_NullOrWhitespaceReceivedSignature_ReturnsFalse(string? receivedSignature)
    {
        var payload = "{\"data\":\"test\"}";

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, receivedSignature!);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_LengthMismatchShorterSignature_ReturnsFalse()
    {
        var payload = "{\"data\":\"test\"}";
        var shortSignature = "v1=abc123";

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, shortSignature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_LengthMismatchLongerSignature_ReturnsFalse()
    {
        var payload = "{\"data\":\"test\"}";
        var longSignature = "v1=" + new string('a', 100);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, longSignature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_SameLengthDifferentCharacters_ReturnsFalse()
    {
        var payload = "{\"data\":\"test\"}";
        var realSignature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        // Tamper last character while preserving length
        var tamperedChar = realSignature[^1] == 'a' ? 'b' : 'a';
        var tamperedSignature = realSignature[..^1] + tamperedChar;

        tamperedSignature.Length.Should().Be(realSignature.Length);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, tamperedSignature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_EmptyPayload_ValidatesCorrectly()
    {
        var payload = string.Empty;
        var signature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, signature);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_MultipleSpacesLeadingToValidSignature_SkipsEmptyTokensAndReturnsTrue()
    {
        var payload = "{\"data\":\"test\"}";
        var realSignature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        var headerWithEmptyTokens = $"v1=dummy     {realSignature}";

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, headerWithEmptyTokens);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_TamperedPrefix_ReturnsFalse()
    {
        var payload = "{\"data\":\"test\"}";
        var realSignature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        var tamperedPrefix = "v2=" + realSignature[3..];

        tamperedPrefix.Length.Should().Be(realSignature.Length);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, tamperedPrefix);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void ComputeSignature_StandardProtocol_MissingWebhookId_ThrowsArgumentException()
    {
        var act1 = () => WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}", WebhookSenderProtocol.Standard, null);
        act1.Should().Throw<ArgumentException>()
            .WithMessage("StandardWebhooks protocol requires a non-empty webhookId (msg_id).*")
            .WithParameterName("webhookId");

        var act2 = () => WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}", WebhookSenderProtocol.Standard, "   ");
        act2.Should().Throw<ArgumentException>()
            .WithMessage("StandardWebhooks protocol requires a non-empty webhookId (msg_id).*")
            .WithParameterName("webhookId");

        var actBytes = () => WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}"u8.ToArray(), WebhookSenderProtocol.Standard, "");
        actBytes.Should().Throw<ArgumentException>()
            .WithMessage("StandardWebhooks protocol requires a non-empty webhookId (msg_id).*")
            .WithParameterName("webhookId");
    }

    [Fact]
    public void ComputeSignature_StandardProtocol_GeneratesBase64Signature()
    {
        var sig = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}", WebhookSenderProtocol.Standard, "msg-001");
        sig.Should().StartWith("v1,");

        var bytesSig = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}"u8.ToArray(), WebhookSenderProtocol.Standard, "msg-001");
        bytesSig.Should().Be(sig);

        // Verification
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", sig, WebhookReceiverProtocol.Standard, "msg-001").Should().BeTrue();
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}"u8.ToArray(), sig, WebhookReceiverProtocol.Standard, "msg-001").Should().BeTrue();

        // Mismatched protocol or missing webhook id returns false
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", sig, WebhookReceiverProtocol.EricksonLopez, "msg-001").Should().BeFalse();
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", sig, WebhookReceiverProtocol.Standard, null).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_MultipleCandidates_BoundaryChecks()
    {
        var validSig = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, "{}");

        // Double space in candidates
        var doubleSpaceSig = $"v1=wrong1  {validSig}";
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", doubleSpaceSig).Should().BeTrue();

        // Exactly 5 candidates where the 5th is valid
        var fiveCandidates = $"v1=bad1 v1=bad2 v1=bad3 v1=bad4 {validSig}";
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", fiveCandidates).Should().BeTrue();

        // 6 candidates exceeds max (5) -> returns false
        var sixCandidates = $"v1=bad0 v1=bad1 v1=bad2 v1=bad3 v1=bad4 {validSig}";
        WebhookSigner.VerifySignature(Secret, DefaultTimestamp, "{}", sixCandidates).Should().BeFalse();
    }

    [Fact]
    public async Task ComputeSignatureAsync_And_VerifySignatureAsync_StreamAndBoundaries()
    {
        var payloadBytes = "{\"test\":123}"u8.ToArray();
        using var stream1 = new System.IO.MemoryStream(payloadBytes);
        var sigStandard = await WebhookSigner.ComputeSignatureAsync(Secret, DefaultTimestamp, stream1, WebhookSenderProtocol.Standard, "msg-123");
        sigStandard.Should().StartWith("v1,");

        using var stream2 = new System.IO.MemoryStream(payloadBytes);
        var sigErickson = await WebhookSigner.ComputeSignatureAsync(Secret, DefaultTimestamp, stream2, WebhookSenderProtocol.EricksonLopez);
        sigErickson.Should().StartWith("v1=");

        // Missing webhookId on async throws
        var actMissingId = async () =>
        {
            using var s = new System.IO.MemoryStream(payloadBytes);
            await WebhookSigner.ComputeSignatureAsync(Secret, DefaultTimestamp, s, WebhookSenderProtocol.Standard, "");
        };
        await actMissingId.Should().ThrowAsync<ArgumentException>()
            .WithMessage("StandardWebhooks protocol requires a non-empty webhookId (msg_id).*")
            .WithParameterName("webhookId");

        // maxPayloadBytes boundary
        using var streamExact = new System.IO.MemoryStream(payloadBytes);
        var sigExact = await WebhookSigner.ComputeSignatureAsync(Secret, DefaultTimestamp, streamExact, WebhookSenderProtocol.EricksonLopez, maxPayloadBytes: payloadBytes.Length);
        sigExact.Should().Be(sigErickson);

        var actOver = async () =>
        {
            using var s = new System.IO.MemoryStream(payloadBytes);
            await WebhookSigner.ComputeSignatureAsync(Secret, DefaultTimestamp, s, WebhookSenderProtocol.EricksonLopez, maxPayloadBytes: payloadBytes.Length - 1);
        };
        await actOver.Should().ThrowAsync<WebhookPayloadTooLargeException>();

        // VerifySignatureAsync with exact boundary
        using var verifyStream = new System.IO.MemoryStream(payloadBytes);
        var verifyExact = await WebhookSigner.VerifySignatureAsync(Secret, DefaultTimestamp, verifyStream, sigErickson, WebhookReceiverProtocol.EricksonLopez, webhookId: null, maxPayloadBytes: payloadBytes.Length);
        verifyExact.Should().BeTrue();

        var actVerifyOver = async () =>
        {
            using var s = new System.IO.MemoryStream(payloadBytes);
            await WebhookSigner.VerifySignatureAsync(Secret, DefaultTimestamp, s, sigErickson, WebhookReceiverProtocol.EricksonLopez, webhookId: null, maxPayloadBytes: payloadBytes.Length - 1);
        };
        await actVerifyOver.Should().ThrowAsync<WebhookPayloadTooLargeException>();
    }

    [Fact]
    public async Task VerifySignatureAsync_SecretRotation_And_DisposalHook()
    {
        var payloadBytes = "{\"data\":\"secret\"}"u8.ToArray();
        var keyOld = "whsec_old_key";
        var keyNew = "whsec_new_key";
        var sigNew = WebhookSigner.ComputeSignature(keyNew, DefaultTimestamp, "{\"data\":\"secret\"}");

        // Multi-key verification (keyNew matches)
        using var stream = new System.IO.MemoryStream(payloadBytes);
        var valid = await WebhookSigner.VerifySignatureAsync(new[] { keyOld, keyNew }, DefaultTimestamp, stream, sigNew, WebhookReceiverProtocol.AutoDetect, null, null);
        valid.Should().BeTrue();

        // Null secret keys
        using var sNull = new System.IO.MemoryStream(payloadBytes);
        var invalidNull = await WebhookSigner.VerifySignatureAsync((System.Collections.Generic.IEnumerable<string>)null!, DefaultTimestamp, sNull, sigNew, WebhookReceiverProtocol.AutoDetect, null, null);
        invalidNull.Should().BeFalse();

        // Null stream
        var invalidNullStream = await WebhookSigner.VerifySignatureAsync(new[] { keyNew }, DefaultTimestamp, null!, sigNew, WebhookReceiverProtocol.AutoDetect, null, null);
        invalidNullStream.Should().BeFalse();

        // Verify hash count and disposal via hook
        var createdHashes = new System.Collections.Generic.List<System.Security.Cryptography.IncrementalHash>();
        WebhookSigner.OnHashCreated = h => createdHashes.Add(h);
        try
        {
            using var s2 = new System.IO.MemoryStream(payloadBytes);
            await WebhookSigner.VerifySignatureAsync(new[] { keyNew }, DefaultTimestamp, s2, sigNew, WebhookReceiverProtocol.EricksonLopez, webhookId: null, maxPayloadBytes: null);
            createdHashes.Should().HaveCount(1);
            var h = createdHashes[0];
            var actDisposed = () => h.AppendData(new byte[1]);
            actDisposed.Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            WebhookSigner.OnHashCreated = null;
        }

        // Protocol Standard creates 1 hash per key (not erickson)
        createdHashes.Clear();
        var sigStd = WebhookSigner.ComputeSignature(keyNew, DefaultTimestamp, "{\"data\":\"secret\"}", WebhookSenderProtocol.Standard, "msg-1");
        WebhookSigner.OnHashCreated = h => createdHashes.Add(h);
        try
        {
            using var s3 = new System.IO.MemoryStream(payloadBytes);
            await WebhookSigner.VerifySignatureAsync(new[] { keyNew }, DefaultTimestamp, s3, sigStd, WebhookReceiverProtocol.Standard, webhookId: "msg-1", maxPayloadBytes: null);
            createdHashes.Should().HaveCount(1);
        }
        finally
        {
            WebhookSigner.OnHashCreated = null;
        }

        // AutoDetect with unknown candidate prefix
        using var sUnknown = new System.IO.MemoryStream(payloadBytes);
        var unknownValid = await WebhookSigner.VerifySignatureAsync(new[] { keyNew }, DefaultTimestamp, sUnknown, "unknown=sig", WebhookReceiverProtocol.Standard, webhookId: null, maxPayloadBytes: null);
        unknownValid.Should().BeFalse();
    }
}
