// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Enforces cryptographic HMAC-SHA256 signature verification and anti-replay validation on ASP.NET Core minimal API endpoints.
/// </summary>
public sealed partial class WebhookEndpointFilter : IEndpointFilter
{
    private readonly ILogger<WebhookEndpointFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEndpointFilter"/> class with an optional logger.
    /// </summary>
    /// <param name="logger">The optional structured logger for recording filter diagnostics.</param>
    public WebhookEndpointFilter(ILogger<WebhookEndpointFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<WebhookEndpointFilter>.Instance;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="next"/> is <see langword="null"/></exception>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var validator = httpContext.RequestServices.GetRequiredService<IWebhookValidator>();

        var validationResult = await validator.ValidateRequestAsync(httpContext, httpContext.RequestAborted);
        if (validationResult.IsFailure)
        {
            LogWebhookEndpointRejected(_logger, httpContext.Request.Path, validationResult.Error.Code, validationResult.Error.Description);

            var statusCode = validationResult.Error.Code == "WebhookValidator.PayloadTooLarge"
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status401Unauthorized;

            return TypedResults.Json(
                new WebhookErrorResponse(validationResult.Error.Code, validationResult.Error.Description),
                WebhookJsonContext.Default.WebhookErrorResponse,
                contentType: "application/json",
                statusCode: statusCode);
        }

        return await next(context);
    }

    [LoggerMessage(EventId = 30, Level = LogLevel.Warning, Message = "Inbound webhook route '{Path}' rejected: [{Code}] {Error}")]
    private static partial void LogWebhookEndpointRejected(ILogger logger, PathString path, string code, string error);
}
