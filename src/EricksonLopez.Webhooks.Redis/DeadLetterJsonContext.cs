// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Webhooks.Redis;

/// <summary>
/// Provides source-generated JSON serialization metadata for <see cref="DeadLetterEnvelope"/> ensuring Native AOT compatibility.
/// </summary>
[JsonSerializable(typeof(DeadLetterEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class DeadLetterJsonContext : JsonSerializerContext
{
}
