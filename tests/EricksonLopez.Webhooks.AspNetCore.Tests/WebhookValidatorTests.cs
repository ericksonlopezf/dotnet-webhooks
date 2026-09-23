// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

public sealed class WebhookValidatorTests
{
    private const string SecretKey = "whsec_test_secret_receiver_key_999";
    private const string SecondarySecretKey = "whsec_secondary_rotation_key_111";
    private readonly WebhookReceiverOptions _options = new()
    {
        SecretKey = SecretKey,
        TimestampTolerance = TimeSpan.FromMinutes(5)
    };

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    private WebhookValidator CreateValidator(WebhookReceiverOptions? options = null) =>
        new(Options.Create(options ?? _options), _timeProvider);

    private static DefaultHttpContext CreateHttpContext(string payload, string? signature, long? timestamp)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";

        if (signature != null)
        {
            context.Request.Headers[WebhookHeaders.Signature] = signature;
        }

        if (timestamp.HasValue)
        {
            context.Request.Headers[WebhookHeaders.Timestamp] = timestamp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return context;
    }

    [Fact]
    public async Task ValidateRequestAsync_ValidSignatureAndTimestamp_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\",\"id\":123}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_MissingSignatureHeader_ReturnsUnauthorizedError()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        var context = CreateHttpContext(payload, signature: null, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.MissingSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_MissingTimestampHeader_ReturnsUnauthorizedError()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\"}";

        var context = CreateHttpContext(payload, signature: "v1=abc", timestamp: null);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidTimestamp");
    }

    [Fact]
    public async Task ValidateRequestAsync_ExpiredTimestamp_ReturnsTimestampExpiredError()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\"}";
        // 10 minutes in the past (exceeds 5 min tolerance)
        var timestamp = _timeProvider.GetUtcNow().AddMinutes(-10).ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.TimestampExpired");
    }

    [Fact]
    public async Task ValidateRequestAsync_InvalidSignature_ReturnsInvalidSignatureError()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = "v1=0000000000000000000000000000000000000000000000000000000000000000";

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_NullHttpContext_ThrowsArgumentNullException()
    {
        var validator = CreateValidator();

        var act = () => validator.ValidateRequestAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateRequestAsync_UnconfiguredSecret_ReturnsFailure()
    {
        var emptyOptions = new WebhookReceiverOptions
        {
            SecretKeys = []
        };
        var validator = CreateValidator(emptyOptions);
        var context = CreateHttpContext("{}", "v1=dummy", 1000);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.UnconfiguredSecret");
    }

    [Fact]
    public async Task ValidateRequestAsync_ModernSecretKeys_ReturnsSuccess()
    {
        var modernOptions = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            TimestampTolerance = TimeSpan.FromMinutes(5)
        };
        var validator = CreateValidator(modernOptions);
        var payload = "{\"event\":\"modern.key\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_EmptyPayload_ValidatesSignatureCorrectly()
    {
        var validator = CreateValidator();
        var payload = string.Empty;
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_FutureTimestampWithinTolerance_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"clock.drift\"}";
        // 2 minutes into the future (within 5 min tolerance)
        var timestamp = _timeProvider.GetUtcNow().AddMinutes(2).ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_FutureTimestampExceedingTolerance_ReturnsTimestampExpiredError()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"clock.drift\"}";
        // 10 minutes into the future (exceeds 5 min tolerance)
        var timestamp = _timeProvider.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.TimestampExpired");
    }

    [Fact]
    public async Task ValidateRequestAsync_MultiSecretRotation_ValidatesSuccessfullyWithSecondaryKey()
    {
        var rotationOptions = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey, SecondarySecretKey],
            TimestampTolerance = TimeSpan.FromMinutes(5)
        };
        var validator = CreateValidator(rotationOptions);

        var payload = "{\"event\":\"payment.captured\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        // Signed with the secondary rotation key
        var signature = WebhookSigner.ComputeSignature(SecondarySecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_MultiSecretRotation_FailsWhenNoKeyMatches()
    {
        var rotationOptions = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey, SecondarySecretKey],
            TimestampTolerance = TimeSpan.FromMinutes(5)
        };
        var validator = CreateValidator(rotationOptions);

        var payload = "{\"event\":\"payment.captured\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature("whsec_unrelated_foreign_key", timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_StandardWebhooksHeaders_ValidatesSuccessfully()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"standard.spec\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.Headers[WebhookHeaders.StandardSignature] = signature;
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new WebhookValidator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task Constructor_NullTimeProvider_DefaultsToSystemTimeProvider()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            TimestampTolerance = TimeSpan.FromMinutes(5)
        };
        var validator = new WebhookValidator(Options.Create(options), timeProvider: null);

        var payload = "{\"event\":\"system.time\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);
        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("123.456")]
    [InlineData(" ")]
    [InlineData("")]
    public async Task ValidateRequestAsync_InvalidNonNumericTimestamp_ReturnsInvalidTimestampError(string badTimestamp)
    {
        var validator = CreateValidator();
        var context = CreateHttpContext("{}", "v1=signature", 12345);
        context.Request.Headers[WebhookHeaders.Timestamp] = badTimestamp;

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidTimestamp");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("    ")]
    public async Task ValidateRequestAsync_WhitespaceSignatureHeader_ReturnsMissingSignatureError(string emptySig)
    {
        var validator = CreateValidator();
        var context = CreateHttpContext("{}", emptySig, 12345);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.MissingSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_BodyStreamPosition_IsResetToZeroForDownstreamReaders()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"downstream.binding\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);
        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        context.Request.Body.Position.Should().Be(0);

        // Verify downstream reader can read full payload
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var readContent = await reader.ReadToEndAsync();
        readContent.Should().Be(payload);
    }

    [Fact]
    public async Task ValidateRequestAsync_ProtocolEricksonLopez_IgnoresStandardHeaders()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            Protocol = WebhookReceiverProtocol.EricksonLopez
        };
        var validator = CreateValidator(options);

        var payload = "{\"event\":\"protocol.erickson\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        // Supply ONLY standard headers
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.Headers[WebhookHeaders.StandardSignature] = signature;
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        // Must reject because protocol is restricted to EricksonLopez
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.MissingSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_ProtocolStandard_IgnoresEricksonLopezHeaders()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            Protocol = WebhookReceiverProtocol.Standard
        };
        var validator = CreateValidator(options);

        var payload = "{\"event\":\"protocol.standard\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        // Supply ONLY proprietary EricksonLopez headers
        var context = CreateHttpContext(payload, signature, timestamp);

        var result = await validator.ValidateRequestAsync(context);

        // Must reject because protocol is restricted to Standard
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.MissingSignature");
    }

    [Fact]
    public async Task ValidateRequestAsync_StandardProtocol_InvalidNonNumericTimestamp_ReturnsInvalidTimestampError()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            Protocol = WebhookReceiverProtocol.Standard
        };
        var validator = CreateValidator(options);

        var payload = "{\"event\":\"standard.badts\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.Headers[WebhookHeaders.StandardSignature] = signature;
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = "invalid_not_long";

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidTimestamp");
    }

    [Fact]
    public async Task ValidateRequestAsync_ProtocolEricksonLopez_InvalidTimestamp_EvaluatesProtocolBranches()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = [SecretKey],
            Protocol = WebhookReceiverProtocol.EricksonLopez
        };
        var validator = CreateValidator(options);

        var payload = "{\"event\":\"erickson.missingts\"}";
        var context = CreateHttpContext(payload, "v1=dummysignature", timestamp: null);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.InvalidTimestamp");
    }

    [Fact]
    public async Task ValidateRequestAsync_WhitespaceProprietarySignature_FallsBackToValidStandardSignature()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"fallback.sig\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        // Proprietary header contains whitespace
        context.Request.Headers[WebhookHeaders.Signature] = "   ";
        // Standard headers contain valid signature and timestamp
        context.Request.Headers[WebhookHeaders.StandardSignature] = signature;
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_ValidProprietaryTimestamp_DoesNotEvaluateOrOverwriteWithStandardTimestamp()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"precedence.ts\"}";
        var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        // Expired timestamp (1 hour ago)
        var expiredTimestamp = _timeProvider.GetUtcNow().AddHours(-1).ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, currentTimestamp, payload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.Headers[WebhookHeaders.Signature] = signature;
        context.Request.Headers[WebhookHeaders.Timestamp] = currentTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // An expired standard timestamp is also present, but must be ignored since proprietary timestamp is valid
        context.Request.Headers[WebhookHeaders.StandardTimestamp] = expiredTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_SkewExactlyEqualToTolerance_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"exact.tolerance\"}";
        // Timestamp differs by exactly 5 minutes (matching TimestampTolerance)
        var exactTimestamp = _timeProvider.GetUtcNow().AddMinutes(-5).ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, exactTimestamp, payload);

        var context = CreateHttpContext(payload, signature, exactTimestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_NonSeekableBodyStream_EnableBufferingAllowsRewindAndValidation()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"stream.buffering\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new NonSeekableTestStream(bytes);
        context.Request.Headers[WebhookHeaders.Signature] = signature;
        context.Request.Headers[WebhookHeaders.Timestamp] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_TimestampMinValue_HandlesUnderflowGracefully()
    {
        var validator = CreateValidator();
        var currentSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var overflowTimestamp = unchecked(currentSeconds - long.MinValue);
        var context = CreateHttpContext("{}", "v1=abc", overflowTimestamp);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.TimestampExpired");
    }

    [Fact]
    public async Task ValidateRequestAsync_WithEricksonLopezDeliveryIdHeader_ExtractsWebhookIdAndValidates()
    {
        var validator = CreateValidator();
        var payload = "{\"event\":\"order.completed\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context = CreateHttpContext(payload, signature, timestamp);
        context.Request.Headers[WebhookHeaders.DeliveryId] = "evt-erickson-123";

        var result = await validator.ValidateRequestAsync(context);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRequestAsync_ContentLengthExceedsMaxPayload_ReturnsPayloadTooLarge()
    {
        var validator = CreateValidator();
        var context = CreateHttpContext("{}", "v1=abc", _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        context.Request.ContentLength = _options.MaxPayloadSizeBytes + 1024;

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.PayloadTooLarge");
    }

    [Fact]
    public async Task ValidateRequestAsync_StreamingPayloadExceedsMaxPayload_CatchesWebhookPayloadTooLargeException()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKey = SecretKey,
            MaxPayloadSizeBytes = 32
        };
        var validator = CreateValidator(options);

        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var largePayload = new string('A', 128);
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, largePayload);

        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(largePayload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = null; // simulate chunked/streaming without Content-Length
        context.Request.Headers[WebhookHeaders.Signature] = signature;
        context.Request.Headers[WebhookHeaders.Timestamp] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await validator.ValidateRequestAsync(context);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("WebhookValidator.PayloadTooLarge");
    }

    [Fact]
    public async Task ValidateRequestAsync_ReplayDetected_ReturnsReplayDetectedError()
    {
        using var replayDetector = new InMemoryWebhookReplayDetector(TimeSpan.FromMinutes(5));
        var validator = new WebhookValidator(Options.Create(_options), _timeProvider, replayDetector);

        var payload = "{\"event\":\"order.created\"}";
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.ComputeSignature(SecretKey, timestamp, payload);

        var context1 = CreateHttpContext(payload, signature, timestamp);
        context1.Request.Headers[WebhookHeaders.DeliveryId] = "evt-dedup-123";

        var result1 = await validator.ValidateRequestAsync(context1);
        result1.IsSuccess.Should().BeTrue();

        var context2 = CreateHttpContext(payload, signature, timestamp);
        context2.Request.Headers[WebhookHeaders.DeliveryId] = "evt-dedup-123";

        var result2 = await validator.ValidateRequestAsync(context2);
        result2.IsFailure.Should().BeTrue();
        result2.Error.Code.Should().Be("WebhookValidator.ReplayDetected");
    }

    private sealed class NonSeekableTestStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableTestStream(byte[] data) => _inner = new MemoryStream(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException("Non-seekable test stream does not support seeking without EnableBuffering");
        }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
