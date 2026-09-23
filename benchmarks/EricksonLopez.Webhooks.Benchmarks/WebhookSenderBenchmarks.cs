// Copyright © Erickson Lopez. MIT License.
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Webhooks.Benchmarks;

/// <summary>
/// Measures <see cref="WebhookSender.SendAsync"/> allocation behavior under controlled conditions.
/// Per ADR-006, the allocation budget requires zero heap allocations from <c>[LoggerMessage]</c>
/// delegates and zero boxing when no OpenTelemetry listener is attached.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("WebhookSender")]
public class WebhookSenderBenchmarks
{
    private WebhookSender _sender = null!;
    private Uri _targetUrl = null!;
    private const string SecretKey = "whsec_benchmark_secret_key_12345";
    private const string EventType = "order.created";
    private const string Payload = "{\"orderId\":\"ord-bench-1\",\"amount\":99.99,\"currency\":\"USD\"}";

    [GlobalSetup]
    public void Setup()
    {
        // Use a no-op handler that returns 200 immediately to isolate sender allocation,
        // not network or response-reading behavior.
        var mockHandler = new NoOpHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        _targetUrl = new Uri("https://benchmark.subscriber.example.com/webhook");

        _sender = new WebhookSender(
            httpClient,
            deadLetterQueue: null,
            options: new WebhookSenderOptions
            {
                Timeout = TimeSpan.FromSeconds(30)
            },
            logger: NullLogger<WebhookSender>.Instance);
    }

    /// <summary>
    /// Measures the full <see cref="WebhookSender.SendAsync"/> end-to-end call with a successful
    /// 200 OK response. Validates zero-allocation logging (via [LoggerMessage]) and zero OTel boxing
    /// when no ActivitySource listener is registered.
    /// </summary>
    [Benchmark(Description = "WebhookSender_SendAsync_Success")]
    public async Task<bool> SendAsync_Success()
    {
        var message = new WebhookMessage(_targetUrl, SecretKey, EventType, "bench-evt-1", System.Text.Encoding.UTF8.GetBytes(Payload));
        var result = await _sender.SendAsync(message, CancellationToken.None);
        return result.Value.IsSuccess;
    }

    /// <summary>
    /// Minimal no-op HTTP handler that returns HTTP 200 OK synchronously,
    /// used to isolate sender-side allocations from HTTP I/O.
    /// </summary>
    private sealed class NoOpHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
