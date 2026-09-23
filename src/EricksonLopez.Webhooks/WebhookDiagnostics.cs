// Copyright © Erickson Lopez. MIT License.
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EricksonLopez.Webhooks;

/// <summary>
/// Provides diagnostic sources, telemetry meters, and activity sources for distributed tracing and observability.
/// </summary>
public static class WebhookDiagnostics
{
    /// <summary>
    /// Represents the diagnostic source name applied to the telemetry <see cref="ActivitySource"/> and <see cref="Meter"/>.
    /// </summary>
    public const string DiagnosticSourceName = "EricksonLopez.Webhooks";

    /// <summary>
    /// Represents the distributed tracing activity source for outbound and inbound webhook operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(DiagnosticSourceName, "1.0.0");

    /// <summary>
    /// Represents the metric meter instance for recording webhook telemetry.
    /// </summary>
    public static readonly Meter Meter = new(DiagnosticSourceName, "1.0.0");

    /// <summary>
    /// Represents a metric counter tracking the total number of outbound HTTP delivery attempts.
    /// </summary>
    public static readonly Counter<long> DeliveriesTotal = Meter.CreateCounter<long>(
        "webhook.deliveries.total",
        description: "Total number of outbound webhook HTTP delivery attempts.");

    /// <summary>
    /// Represents a metric counter tracking failed webhook deliveries routed to a dead-letter sink.
    /// </summary>
    public static readonly Counter<long> DeadLetterEscalationsTotal = Meter.CreateCounter<long>(
        "webhook.dead_letter.total",
        description: "Total number of failed webhook deliveries escalated to the dead-letter sink.");

    /// <summary>
    /// Represents a metric histogram tracking the elapsed duration of HTTP delivery attempts in milliseconds.
    /// </summary>
    public static readonly Histogram<double> DeliveryDuration = Meter.CreateHistogram<double>(
        "webhook.delivery.duration",
        unit: "ms",
        description: "Duration of webhook HTTP delivery attempts in milliseconds.");

    /// <summary>
    /// Represents a metric counter tracking inbound webhook validation attempts.
    /// </summary>
    public static readonly Counter<long> InboundValidationsTotal = Meter.CreateCounter<long>(
        "webhook.inbound.validations.total",
        description: "Total number of inbound webhook request validation attempts.");
}
