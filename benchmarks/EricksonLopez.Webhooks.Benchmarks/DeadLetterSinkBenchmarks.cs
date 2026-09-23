// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace EricksonLopez.Webhooks.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("DeadLetterSink")]
public class DeadLetterSinkBenchmarks
{
    private InMemoryWebhookDeadLetterSink _sink = null!;
    private Uri _url = null!;
    private WebhookPayload _payload = null!;
    private WebhookDeliveryResult _result;

    [GlobalSetup]
    public void Setup()
    {
        _sink = new InMemoryWebhookDeadLetterSink();
        _url = new Uri("https://api.subscriber.com/webhook/dead-letter");
        _payload = new WebhookPayload("del-benchmark-1", "order.created", "{\"status\":\"failed\"}", DateTimeOffset.UtcNow, 3);
        _result = new WebhookDeliveryResult(false, 500, 3, TimeSpan.FromMilliseconds(50), "Internal Server Error");
    }

    [Benchmark(Description = "DeadLetterSink_EnqueueAsync")]
    public Task EnqueueAsync()
    {
        return _sink.EnqueueAsync(_url, _payload, _result);
    }
}
