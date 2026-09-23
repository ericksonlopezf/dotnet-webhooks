// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Represents an error response payload for unauthorized webhook rejections.
/// </summary>
/// <param name="Code">The error code identifying the validation failure.</param>
/// <param name="Error">The descriptive explanation of the rejection reason.</param>
internal sealed record WebhookErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("error")] string Error);

[JsonSerializable(typeof(WebhookErrorResponse))]
internal sealed partial class WebhookJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Intercepts and validates inbound webhook HTTP requests within the ASP.NET Core pipeline.
/// </summary>
public sealed partial class WebhookReceiverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WebhookReceiverOptions _options;
    private readonly ILogger<WebhookReceiverMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookReceiverMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate in the request pipeline.</param>
    /// <param name="options">The accessor for receiver configuration options.</param>
    public WebhookReceiverMiddleware(
        RequestDelegate next,
        IOptions<WebhookReceiverOptions> options)
        : this(next, options, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookReceiverMiddleware"/> class with structured logging.
    /// </summary>
    /// <param name="next">The next middleware delegate in the request pipeline.</param>
    /// <param name="options">The accessor for receiver configuration options.</param>
    /// <param name="logger">The structured logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> or <paramref name="options"/> is <see langword="null"/></exception>
    public WebhookReceiverMiddleware(
        RequestDelegate next,
        IOptions<WebhookReceiverOptions> options,
        ILogger<WebhookReceiverMiddleware>? logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<WebhookReceiverMiddleware>.Instance;
    }

    /// <summary>
    /// Processes an HTTP request through the webhook validation pipeline.
    /// </summary>
    /// <remarks>
    /// Requests whose path does not match <see cref="WebhookReceiverOptions.RoutePath"/> bypass validation.
    /// Failed validations write an HTTP error status code and terminate request execution.
    /// </remarks>
    /// <param name="context">The HTTP context representing the incoming request.</param>
    /// <param name="validator">The validator responsible for signature and timestamp verification.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="validator"/> is <see langword="null"/></exception>
    public async Task InvokeAsync(HttpContext context, IWebhookValidator validator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(validator);

        if (!context.Request.Path.StartsWithSegments(_options.RoutePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (HttpMethods.IsOptions(context.Request.Method) ||
            HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (_options.ValidatePostOnly && !HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var validationResult = await validator.ValidateRequestAsync(context, context.RequestAborted);
        if (validationResult.IsFailure)
        {
            LogWebhookRejected(_logger, context.Request.Path, validationResult.Error.Code, validationResult.Error.Description);

            context.Response.StatusCode = validationResult.Error.Code == "WebhookValidator.PayloadTooLarge"
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var payload = new WebhookErrorResponse(validationResult.Error.Code, validationResult.Error.Description);
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WebhookJsonContext.Default.WebhookErrorResponse);

            await context.Response.Body.WriteAsync(utf8Bytes, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context);
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "Inbound webhook to '{Path}' was rejected: [{Code}] {Error}")]
    private static partial void LogWebhookRejected(ILogger logger, PathString path, string code, string error);
}
