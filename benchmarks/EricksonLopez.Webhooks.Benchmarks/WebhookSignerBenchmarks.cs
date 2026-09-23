// Copyright © Erickson Lopez. MIT License.
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace EricksonLopez.Webhooks.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("WebhookSigner")]
public class WebhookSignerBenchmarks
{
    private const string Secret = "whsec_super_secret_production_benchmark_key_12345";
    private const long Timestamp = 1700000000L;

    private string _smallPayload = string.Empty;
    private string _mediumPayload = string.Empty;
    private string _smallSignature = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _smallPayload = "{\"event\":\"order.created\",\"id\":\"ord-12345\",\"amount\":99.99}";
        _mediumPayload = "{\"event\":\"order.batch\",\"items\":[" + string.Join(",", Enumerable.Range(1, 100).Select(i => $"{{\"id\":{i},\"sku\":\"SKU-{i}\"}}")) + "]}";
        _smallSignature = WebhookSigner.ComputeSignature(Secret, Timestamp, _smallPayload);
    }

    [Benchmark(Description = "ComputeSignature_SmallPayload")]
    public string ComputeSignature_SmallPayload()
    {
        return WebhookSigner.ComputeSignature(Secret, Timestamp, _smallPayload);
    }

    [Benchmark(Description = "ComputeSignature_MediumPayload")]
    public string ComputeSignature_MediumPayload()
    {
        return WebhookSigner.ComputeSignature(Secret, Timestamp, _mediumPayload);
    }

    [Benchmark(Description = "VerifySignature_ConstantTime")]
    public bool VerifySignature_ConstantTime()
    {
        return WebhookSigner.VerifySignature(Secret, Timestamp, _smallPayload, _smallSignature);
    }
}
