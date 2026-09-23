// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Webhooks.Sample;

[JsonSerializable(typeof(OrderEvent))]
internal sealed partial class SampleJsonContext : JsonSerializerContext
{
}
