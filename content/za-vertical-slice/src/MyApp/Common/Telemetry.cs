using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Common;

/// <summary>
/// The app's OTel source and meter name, plus a shared <see cref="ActivitySource"/> and
/// <see cref="Meter"/> for hand-written instrumentation. Program.cs's
/// <c>AddOpenTelemetry()</c> block subscribes to the name with
/// <c>AddSource(Telemetry.SourceName)</c> and <c>AddMeter(Telemetry.SourceName)</c>, next to
/// its environment-gated exporters.
/// <para>
/// Two ways to emit under that name. Put <c>[Instrument(Telemetry.SourceName)]</c> on an
/// interface and ZA.Telemetry generates the proxy, as the CreateCustomer slice does for
/// <c>ICustomerWriter</c>; or start spans and create instruments yourself from the
/// <see cref="ActivitySource"/> and <see cref="Meter"/> below.
/// </para>
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
    /// ZA.Telemetry proxies create their own <see cref="ActivitySource"/> from the name passed
    /// to <c>[Instrument]</c>; with <see cref="SourceName"/> the same subscription covers both.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// App-wide <see cref="Meter"/>. Slice code creates counters/histograms via
    /// <c>Telemetry.Meter.CreateCounter&lt;long&gt;("orders.placed")</c>. ZA.Telemetry's
    /// <c>[Count]</c> and <c>[Histogram]</c> instruments live on a meter the proxy creates from
    /// the <c>[Instrument]</c> name; with <see cref="SourceName"/>, <c>AddMeter</c> covers both.
    /// </summary>
    public static readonly Meter Meter = new(SourceName);
}
