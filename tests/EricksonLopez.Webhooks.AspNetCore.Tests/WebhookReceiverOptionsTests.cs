// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

public sealed class WebhookReceiverOptionsTests
{
    [Fact]
    public void Properties_DefaultValues_MatchSpecification()
    {
        var options = new WebhookReceiverOptions();
        options.SecretKey.Should().BeEmpty();
        options.SecretKeys.Should().BeEmpty();
        options.TimestampTolerance.Should().Be(TimeSpan.FromMinutes(5));
        options.RoutePath.Should().Be("/api/webhooks");
        options.Protocol.Should().Be(WebhookReceiverProtocol.AutoDetect);
    }

    [Fact]
    public void SecretKey_SetNull_AssignsEmptyString()
    {
        var options = new WebhookReceiverOptions();
        options.SecretKey = null!;
        options.SecretKey.Should().Be(string.Empty);
    }

    [Fact]
    public void SecretKey_SetCustomValue_StoresValue()
    {
        var options = new WebhookReceiverOptions();
        options.SecretKey = "whsec_custom_value";
        options.SecretKey.Should().Be("whsec_custom_value");
    }

    [Fact]
    public void Properties_CustomAssignment_PreservesAssignedValues()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key1", "key2"),
            TimestampTolerance = TimeSpan.FromSeconds(30),
            RoutePath = "/custom/webhooks/endpoint",
            Protocol = WebhookReceiverProtocol.Standard
        };

        options.SecretKeys.Should().ContainInOrder("key1", "key2");
        options.TimestampTolerance.Should().Be(TimeSpan.FromSeconds(30));
        options.RoutePath.Should().Be("/custom/webhooks/endpoint");
        options.Protocol.Should().Be(WebhookReceiverProtocol.Standard);
    }

    [Fact]
    public void GetActiveSecretKeys_WhenOnlyLegacySecretKeyConfigured_YieldsLegacyKey()
    {
        var options = new WebhookReceiverOptions();
        options.SecretKey = "whsec_legacy_key";

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(1);
        keys[0].Should().Be("whsec_legacy_key");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void GetActiveSecretKeys_WhenLegacySecretKeyIsWhitespaceOrEmpty_YieldsEmpty(string emptyKey)
    {
        var options = new WebhookReceiverOptions();
        options.SecretKey = emptyKey;

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().BeEmpty();
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysConfiguredWithoutLegacy_YieldsAllSecretKeys()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key_primary", "key_secondary")
        };

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(2);
        keys.Should().ContainInOrder("key_primary", "key_secondary");
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysContainsWhitespaceAndNull_FiltersThemOut()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key_valid_1", null!, "", "   ", "key_valid_2")
        };

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(2);
        keys.Should().ContainInOrder("key_valid_1", "key_valid_2");
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysContainsLegacyKey_DoesNotDuplicateLegacyKey()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key_primary", "whsec_shared_legacy", "key_secondary")
        };
        options.SecretKey = "whsec_shared_legacy";

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(3);
        keys.Should().ContainInOrder("key_primary", "whsec_shared_legacy", "key_secondary");
        keys.Count(k => k == "whsec_shared_legacy").Should().Be(1);
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysDoesNotContainLegacyKey_AppendsLegacyKey()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key_new_1", "key_new_2")
        };
        options.SecretKey = "whsec_legacy_fallback";

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(3);
        keys.Should().ContainInOrder("key_new_1", "key_new_2", "whsec_legacy_fallback");
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysConfiguredAndLegacyKeyWhitespace_DoesNotAppendLegacyKey()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = ImmutableList.Create("key_new_1")
        };
        options.SecretKey = "   ";

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(1);
        keys[0].Should().Be("key_new_1");
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysNull_FallbackToLegacyKeyIfPresent()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = null!
        };
        options.SecretKey = "whsec_legacy_only";

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().HaveCount(1);
        keys[0].Should().Be("whsec_legacy_only");
    }

    [Fact]
    public void GetActiveSecretKeys_WhenSecretKeysNullAndLegacyNullOrEmpty_YieldsEmpty()
    {
        var options = new WebhookReceiverOptions
        {
            SecretKeys = null!
        };

        var keys = options.GetActiveSecretKeys().ToList();

        keys.Should().BeEmpty();
    }

    [Fact]
    public void AddSecret_ValidKey_AppendsToSecretKeysAndReturnsThis()
    {
        var options = new WebhookReceiverOptions();
        var returnedOptions = options.AddSecret("whsec_new_key");

        returnedOptions.Should().BeSameAs(options);
        options.SecretKeys.Should().ContainSingle().Which.Should().Be("whsec_new_key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSecret_NullOrWhitespace_ThrowsArgumentException(string? invalidSecret)
    {
        var options = new WebhookReceiverOptions();
        var act = () => options.AddSecret(invalidSecret!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("secretKey")
            .WithMessage("Secret key cannot be null or whitespace.*");
    }

    [Fact]
    public void MaxPayloadSizeBytes_ValidValue_AssignsSuccessfully()
    {
        var options = new WebhookReceiverOptions { MaxPayloadSizeBytes = 2048 };
        options.MaxPayloadSizeBytes.Should().Be(2048);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void MaxPayloadSizeBytes_ZeroOrNegative_ThrowsArgumentOutOfRangeException(int invalidSize)
    {
        var options = new WebhookReceiverOptions();
        var act = () => options.MaxPayloadSizeBytes = invalidSize;

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("MaxPayloadSizeBytes must be greater than zero.*");
    }

    [Fact]
    public void ValidatePostOnly_SetAndGet_ReflectsValue()
    {
        var options = new WebhookReceiverOptions();
        options.ValidatePostOnly.Should().BeFalse();

        options.ValidatePostOnly = true;
        options.ValidatePostOnly.Should().BeTrue();
    }
}

