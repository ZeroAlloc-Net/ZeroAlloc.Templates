using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Common;

/// <summary>
/// Shared <see cref="ActivitySource"/> and <see cref="Meter"/> for slice-level
/// instrumentation. Each slice that wants a custom trace span or counter pulls
/// these constants instead of newing its own — keeps the OTel resource name
/// stable across the whole app. Program.cs's <c>AddOpenTelemetry()</c> block
/// subscribes to it with <c>AddSource(Telemetry.SourceName)</c> and
/// <c>AddMeter(Telemetry.SourceName)</c>, next to its environment-gated exporters.
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// OTel source / meter name. Program.cs passes it to <c>AddSource</c> and
    /// <c>AddMeter</c>, so renaming it here renames the subscription too.
    /// </summary>
    public const string SourceName = "MyApp";

    /// <summary>
    /// App-wide <see cref="ActivitySource"/>. Slice code starts spans via
    /// <c>using var activity = Telemetry.ActivitySource.StartActivity("place_order")</c>.
    /// ZA.Telemetry's <c>[Trace]</c> attribute also targets this source by default.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// App-wide <see cref="Meter"/>. Slice code creates counters/histograms via
    /// <c>Telemetry.Meter.CreateCounter&lt;long&gt;("orders.placed")</c>. ZA.Telemetry's
    /// <c>[Count]</c> / <c>[Histogram]</c> attributes also target this meter by default.
    /// </summary>
    public static readonly Meter Meter = new(SourceName);
}
