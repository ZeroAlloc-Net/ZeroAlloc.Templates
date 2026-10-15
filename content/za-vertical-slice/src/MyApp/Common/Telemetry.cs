using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Common;

/// <summary>
/// The OTel source and meter names Program.cs's <c>AddOpenTelemetry()</c> block subscribes to,
/// plus a shared <see cref="ActivitySource"/> and <see cref="Meter"/> for hand-written
/// instrumentation.
/// <para>
/// Cross-cutting telemetry comes from the mediator pipeline, so slices stay self-contained:
/// every <c>Send</c> opens a <c>mediator.send</c> span, and ZeroAlloc.Mediator.Telemetry's
/// pipeline behavior counts and times it, all under <see cref="MediatorSourceName"/>. A slice
/// needs no telemetry code of its own. When one wants a span or counter for its own work, it
/// uses the <see cref="ActivitySource"/> and <see cref="Meter"/> below instead of newing its own,
/// which keeps the OTel name stable across the whole app.
/// </para>
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// The app's own OTel source / meter name. Program.cs passes it to <c>AddSource</c> and
    /// <c>AddMeter</c>, so renaming it here renames the subscription too.
    /// </summary>
    public const string SourceName = "MyApp";

    /// <summary>
    /// ZeroAlloc.Mediator's activity source and meter name. The generated <c>Send</c> opens its
    /// <c>mediator.send</c> span on this source; ZeroAlloc.Mediator.Telemetry records
    /// <c>mediator.requests_total</c> and <c>mediator.request_duration_ms</c> on this meter.
    /// Fixed by the library: Program.cs subscribes to it, it is not the app's to rename.
    /// </summary>
    public const string MediatorSourceName = "ZeroAlloc.Mediator";

    /// <summary>
    /// App-wide <see cref="ActivitySource"/>. Slice code starts spans via
    /// <c>using var activity = Telemetry.ActivitySource.StartActivity("place_order")</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// App-wide <see cref="Meter"/>. Slice code creates counters/histograms via
    /// <c>Telemetry.Meter.CreateCounter&lt;long&gt;("orders.placed")</c>.
    /// </summary>
    public static readonly Meter Meter = new(SourceName);
}
