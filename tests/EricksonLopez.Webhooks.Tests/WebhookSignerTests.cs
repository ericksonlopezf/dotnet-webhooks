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
    public void VerifySignature_TamperedPrefix_ReturnsFalse()
    {
        var payload = "{\"data\":\"test\"}";
        var realSignature = WebhookSigner.ComputeSignature(Secret, DefaultTimestamp, payload);
        var tamperedPrefix = "v2=" + realSignature[3..];

        tamperedPrefix.Length.Should().Be(realSignature.Length);

        var isValid = WebhookSigner.VerifySignature(Secret, DefaultTimestamp, payload, tamperedPrefix);

        isValid.Should().BeFalse();
    }
}
