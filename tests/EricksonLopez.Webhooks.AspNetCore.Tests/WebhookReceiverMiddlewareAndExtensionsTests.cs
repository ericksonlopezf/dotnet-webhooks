// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

public sealed class WebhookReceiverMiddlewareAndExtensionsTests
{
    private sealed class FakeWebhookValidator : IWebhookValidator
    {
        public Result<bool> ResultToReturn { get; set; } = Result<bool>.Success(true);
        public bool WasCalled { get; private set; }

        public Task<Result<bool>> ValidateRequestAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(ResultToReturn);
        }
    }

    [Fact]
    public void MiddlewareConstructor_NullNext_ThrowsArgumentNullException()
    {
        var options = Options.Create(new WebhookReceiverOptions());
        var act = () => new WebhookReceiverMiddleware(null!, options);

        act.Should().Throw<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public void MiddlewareConstructor_NullOptions_ThrowsArgumentNullException()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var act = () => new WebhookReceiverMiddleware(next, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new WebhookReceiverMiddleware(next, Options.Create(new WebhookReceiverOptions()));
        var validator = new FakeWebhookValidator();

        var act = () => middleware.InvokeAsync(null!, validator);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task InvokeAsync_NullValidator_ThrowsArgumentNullException()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new WebhookReceiverMiddleware(next, Options.Create(new WebhookReceiverOptions()));
        var context = new DefaultHttpContext();

        var act = () => middleware.InvokeAsync(context, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("validator");
    }

    [Fact]
    public async Task InvokeAsync_RouteDoesNotMatch_BypassesValidationAndCallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Options.Create(new WebhookReceiverOptions { RoutePath = "/api/webhooks" });
        var middleware = new WebhookReceiverMiddleware(next, options);
        var validator = new FakeWebhookValidator();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/other-endpoint";

        await middleware.InvokeAsync(context, validator);

        nextCalled.Should().BeTrue();
        validator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ValidatePostOnlyEnabled_NonPostMethod_BypassesValidationAndCallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Options.Create(new WebhookReceiverOptions { RoutePath = "/api/webhooks", ValidatePostOnly = true });
        var middleware = new WebhookReceiverMiddleware(next, options);
        var validator = new FakeWebhookValidator();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks";
        context.Request.Method = HttpMethods.Put;

        await middleware.InvokeAsync(context, validator);

        nextCalled.Should().BeTrue();
        validator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ValidationSucceeds_CallsNextDelegate()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Options.Create(new WebhookReceiverOptions { RoutePath = "/api/webhooks" });
        var middleware = new WebhookReceiverMiddleware(next, options);
        var validator = new FakeWebhookValidator { ResultToReturn = Result<bool>.Success(true) };

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks";

        await middleware.InvokeAsync(context, validator);

        nextCalled.Should().BeTrue();
        validator.WasCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ValidationFails_ShortCircuitsWith401AndJsonPayload()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Options.Create(new WebhookReceiverOptions { RoutePath = "/api/webhooks" });
        var middleware = new WebhookReceiverMiddleware(next, options);
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Unauthorized("WebhookValidator.InvalidSignature", "Signature verification failed.")
        };

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks";
        var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        await middleware.InvokeAsync(context, validator);

        nextCalled.Should().BeFalse();
        validator.WasCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().Be("application/json");

        responseBodyStream.Position = 0;
        using var reader = new StreamReader(responseBodyStream);
        var responseText = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseText);
        doc.RootElement.GetProperty("code").GetString().Should().Be("WebhookValidator.InvalidSignature");
        doc.RootElement.GetProperty("error").GetString().Should().Be("Signature verification failed.");
    }

    [Fact]
    public void AddWebhookReceiver_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddWebhookReceiver(opts => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddWebhookReceiver_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddWebhookReceiver(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AddWebhookReceiver_ValidArguments_RegistersOptionsAndValidatorSingleton()
    {
        var services = new ServiceCollection();
        var returnedServices = services.AddWebhookReceiver(opts =>
        {
            opts.SecretKeys = ["whsec_test_registered_key"];
            opts.RoutePath = "/webhooks/incoming";
            opts.Protocol = WebhookReceiverProtocol.Standard;
        });

        returnedServices.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetService<IOptions<WebhookReceiverOptions>>();
        options.Should().NotBeNull();
        options!.Value.SecretKeys.Should().ContainSingle().Which.Should().Be("whsec_test_registered_key");
        options.Value.RoutePath.Should().Be("/webhooks/incoming");
        options.Value.Protocol.Should().Be(WebhookReceiverProtocol.Standard);

        var validator = provider.GetService<IWebhookValidator>();
        validator.Should().NotBeNull();
        validator.Should().BeOfType<WebhookValidator>();
    }

    [Fact]
    public void UseWebhookReceiver_NullApp_ThrowsArgumentNullException()
    {
        IApplicationBuilder app = null!;
        var act = () => app.UseWebhookReceiver();

        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseWebhookReceiver_ValidApp_RegistersMiddlewareInPipeline()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        var returnedApp = app.UseWebhookReceiver();

        returnedApp.Should().BeSameAs(app);
    }

    [Fact]
    public void AddInMemoryReplayDetector_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddInMemoryReplayDetector();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddInMemoryReplayDetector_DefaultRetention_RegistersDetectorSingleton()
    {
        var services = new ServiceCollection();
        var returnedServices = services.AddInMemoryReplayDetector();

        returnedServices.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();
        var detector = provider.GetService<IWebhookReplayDetector>();

        detector.Should().NotBeNull();
        detector.Should().BeOfType<InMemoryWebhookReplayDetector>();
    }

    [Fact]
    public void AddInMemoryReplayDetector_ExplicitRetention_RegistersDetectorSingleton()
    {
        var services = new ServiceCollection();
        var returnedServices = services.AddInMemoryReplayDetector(TimeSpan.FromMinutes(10));

        returnedServices.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();
        var detector = provider.GetService<IWebhookReplayDetector>();

        detector.Should().NotBeNull();
        detector.Should().BeOfType<InMemoryWebhookReplayDetector>();
    }
}

