// Copyright © Erickson Lopez. MIT License.
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Webhooks.Tests;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _asyncHandler;

    public int CallCount { get; private set; }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _asyncHandler = (req, _) => Task.FromResult(handler(req));
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncHandler)
    {
        _asyncHandler = asyncHandler ?? throw new ArgumentNullException(nameof(asyncHandler));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return await _asyncHandler(request, cancellationToken).ConfigureAwait(false);
    }
}
