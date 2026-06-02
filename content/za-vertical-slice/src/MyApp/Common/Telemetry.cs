using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MyApp.Common;

/// <summary>
/// Shared <see cref="ActivitySource"/> and <see cref="Meter"/> for slice-level
/// instrumentation. Each slice that wants a custom trace span or counter pulls
/// these constants instead of newing its own — keeps the OTel resource name
/// stable across the whole app and matches the source registered in Program.cs's
/// <c>AddOpenTelemetry().WithTracing(t => t.AddSource("MyApp"))</c> block.
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// OTel source / meter name. Matches <c>AddSource</c> + <c>AddMeter</c> calls
    /// in Program.cs — changing this constant requires updating both.
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

/// <summary>
/// Composition helper: bundle the OTel tracing + metrics registration in one place
/// so Program.cs can call <c>builder.Services.AddMyAppTelemetry()</c> instead of
/// repeating the With-Tracing / With-Metrics block. Configures the dev-default
/// console exporter — production deployments override via the
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var (the OTel SDK reads it natively).
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core + HttpClient instrumentation, plus the
    /// <c>MyApp</c> and <c>ZeroAlloc.Mediator</c> activity sources. Mirrors
    /// za-clean's Program.cs OTel block — extracted here so Program.cs in the
    /// vertical-slice template stays under ~50 lines of composition.
    /// Post the ZA.ORM swap there is no EF Core instrumentation to add;
    /// ZA.ORM's emitted code surfaces DB activity via the standard ADO.NET
    /// diagnostic source, which AspNetCoreInstrumentation already correlates.
    /// </summary>
    public static IServiceCollection AddMyAppTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("ZeroAlloc.Mediator")
                .AddSource(Telemetry.SourceName)
                .AddConsoleExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddMeter(Telemetry.SourceName)
                .AddConsoleExporter());

        return services;
    }
}
