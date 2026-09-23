// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EricksonLopez.Webhooks.AspNetCore.Tests;

public sealed class WebhookEndpointFilterTests
{
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
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

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        private readonly HttpContext _httpContext;
        private readonly IList<object?> _arguments;

        public TestEndpointFilterInvocationContext(HttpContext httpContext, IList<object?>? arguments = null)
        {
            _httpContext = httpContext;
            _arguments = arguments ?? Array.Empty<object?>();
        }

        public override HttpContext HttpContext => _httpContext;

        public override IList<object?> Arguments => _arguments;

        public override T GetArgument<T>(int index) => (T)_arguments[index]!;
    }

    [Fact]
    public void Constructor_Default_CreatesInstanceWithNullLogger()
    {
        var filter = new WebhookEndpointFilter();

        filter.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var filter = new WebhookEndpointFilter();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("ok");

        Func<Task> act = async () => await filter.InvokeAsync(null!, next);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Fact]
    public async Task InvokeAsync_NullNext_ThrowsArgumentNullException()
    {
        var filter = new WebhookEndpointFilter();
        var httpContext = new DefaultHttpContext();
        var context = new TestEndpointFilterInvocationContext(httpContext);

        Func<Task> act = async () => await filter.InvokeAsync(context, null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("next");
    }

    [Fact]
    public async Task InvokeAsync_ValidationSucceeds_ExecutesNextDelegateAndReturnsValue()
    {
        var validator = new FakeWebhookValidator { ResultToReturn = Result<bool>.Success(true) };
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookValidator>(validator);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Path = "/webhooks/receive";

        var context = new TestEndpointFilterInvocationContext(httpContext);
        var filter = new WebhookEndpointFilter();

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success_payload");
        };

        var result = await filter.InvokeAsync(context, next);

        validator.WasCalled.Should().BeTrue();
        nextCalled.Should().BeTrue();
        result.Should().Be("success_payload");
    }

    [Fact]
    public async Task InvokeAsync_ValidationFails_InvalidSignature_Returns401JsonResult()
    {
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Unauthorized("WebhookValidator.InvalidSignature", "Signature mismatch.")
        };
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookValidator>(validator);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Path = "/webhooks/receive";

        var context = new TestEndpointFilterInvocationContext(httpContext);
        var filter = new WebhookEndpointFilter();

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("never_reached");
        };

        var result = await filter.InvokeAsync(context, next);

        validator.WasCalled.Should().BeTrue();
        nextCalled.Should().BeFalse();

        result.Should().NotBeNull();
        var jsonResult = result.Should().BeOfType<JsonHttpResult<WebhookErrorResponse>>().Subject;
        jsonResult.ContentType.Should().Be("application/json");
        jsonResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        jsonResult.Value.Should().NotBeNull();
        jsonResult.Value!.Code.Should().Be("WebhookValidator.InvalidSignature");
        jsonResult.Value.Error.Should().Be("Signature mismatch.");
    }

    [Fact]
    public async Task InvokeAsync_WithCustomLogger_LogsWarningOnFailure()
    {
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Unauthorized("WebhookValidator.InvalidSignature", "Signature mismatch.")
        };
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookValidator>(validator);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Path = "/webhooks/receive";

        var context = new TestEndpointFilterInvocationContext(httpContext);
        var logger = new TestLogger<WebhookEndpointFilter>();
        var filter = new WebhookEndpointFilter(logger);

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        result.Should().NotBeNull();
        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("Inbound webhook route '/webhooks/receive' rejected: [WebhookValidator.InvalidSignature] Signature mismatch.");
    }

    [Fact]
    public async Task InvokeAsync_ValidationFails_PayloadTooLarge_Returns413JsonResult()
    {
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Validation("WebhookValidator.PayloadTooLarge", "Payload exceeded 262144 bytes limit.")
        };
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookValidator>(validator);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Path = "/webhooks/receive";

        var context = new TestEndpointFilterInvocationContext(httpContext);
        var filter = new WebhookEndpointFilter();

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("never_reached");
        };

        var result = await filter.InvokeAsync(context, next);

        validator.WasCalled.Should().BeTrue();
        nextCalled.Should().BeFalse();

        result.Should().NotBeNull();
        var jsonResult = result.Should().BeOfType<JsonHttpResult<WebhookErrorResponse>>().Subject;
        jsonResult.ContentType.Should().Be("application/json");
        jsonResult.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        jsonResult.Value.Should().NotBeNull();
        jsonResult.Value!.Code.Should().Be("WebhookValidator.PayloadTooLarge");
        jsonResult.Value.Error.Should().Be("Payload exceeded 262144 bytes limit.");
    }

    [Fact]
    public void RequireWebhookSignature_RouteHandlerBuilder_Null_ThrowsArgumentNullException()
    {
        RouteHandlerBuilder builder = null!;

        var act = () => builder.RequireWebhookSignature();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("builder");
    }

    [Fact]
    public void RequireWebhookSignature_RouteGroupBuilder_Null_ThrowsArgumentNullException()
    {
        RouteGroupBuilder builder = null!;

        var act = () => builder.RequireWebhookSignature();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("builder");
    }

    [Fact]
    public async Task RequireWebhookSignature_RouteHandlerBuilder_RegistersEndpointFilter()
    {
        var appBuilder = WebApplication.CreateBuilder();
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Unauthorized("WebhookValidator.InvalidSignature", "Signature mismatch.")
        };
        appBuilder.Services.AddSingleton<IWebhookValidator>(validator);
        var app = appBuilder.Build();

        var routeHandler = app.MapPost("/webhook", () => Results.Ok());
        var returnedBuilder = routeHandler.RequireWebhookSignature();

        returnedBuilder.Should().BeSameAs(routeHandler);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(ds => ds.Endpoints).First();
        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        httpContext.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(httpContext);

        validator.WasCalled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task RequireWebhookSignature_RouteGroupBuilder_RegistersEndpointFilter()
    {
        var appBuilder = WebApplication.CreateBuilder();
        var validator = new FakeWebhookValidator
        {
            ResultToReturn = Error.Unauthorized("WebhookValidator.InvalidSignature", "Signature mismatch.")
        };
        appBuilder.Services.AddSingleton<IWebhookValidator>(validator);
        var app = appBuilder.Build();

        var routeGroup = app.MapGroup("/webhooks");
        var returnedGroup = routeGroup.RequireWebhookSignature();

        returnedGroup.Should().BeSameAs(routeGroup);

        routeGroup.MapPost("/receive", () => Results.Ok());

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(ds => ds.Endpoints).First();
        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        httpContext.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(httpContext);

        validator.WasCalled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}
