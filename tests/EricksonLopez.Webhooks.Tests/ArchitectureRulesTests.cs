// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.Webhooks.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly System.Reflection.Assembly WebhooksAssembly = typeof(WebhookSender).Assembly;

    [Fact]
    public void CoreEngine_MustNotDependOnAspNetCore_PreservesCleanSeparation()
    {
        var result = Types.InAssembly(WebhooksAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PublicClasses_MustBeSealedOrStatic_EnforcesNativeAotInvariants()
    {
        var result = Types.InAssembly(WebhooksAssembly)
            .That()
            .AreClasses()
            .And()
            .ArePublic()
            .Should()
            .BeSealed()
            .Or()
            .BeStatic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void CoreTypes_MustResideInWebhooksNamespace()
    {
        var result = Types.InAssembly(WebhooksAssembly)
            .That()
            .ArePublic()
            .Should()
            .ResideInNamespace("EricksonLopez.Webhooks")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Interfaces_MustFollowPrefixConvention_AndBePublic()
    {
        var result = Types.InAssembly(WebhooksAssembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .And()
            .BePublic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ExtensionClasses_MustBeStatic()
    {
        var result = Types.InAssembly(WebhooksAssembly)
            .That()
            .HaveNameEndingWith("Extensions")
            .Should()
            .BeStatic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
