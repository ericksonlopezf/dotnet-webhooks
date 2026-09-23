// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Http;

namespace EricksonLopez.Webhooks.AspNetCore;

/// <summary>
/// Defines a contract for validating the authenticity, freshness, and replay status of inbound webhook HTTP requests.
/// </summary>
public interface IWebhookValidator
{
    /// <summary>
    /// Validates that an incoming HTTP request contains a fresh timestamp, an authentic cryptographic signature, and is not a replay attack.
    /// </summary>
    /// <param name="httpContext">The HTTP context representing the incoming webhook request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful result if the request is authentic;
    /// otherwise, an error describing the validation or security failure.
    /// </returns>
    Task<Result<bool>> ValidateRequestAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}
