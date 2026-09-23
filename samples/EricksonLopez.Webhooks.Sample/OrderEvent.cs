// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Webhooks.Sample;

/// <summary>
/// Sample domain event for Native AOT strongly-typed webhook dispatching.
/// </summary>
public sealed record OrderEvent(string OrderId, decimal Amount, string CustomerEmail);
