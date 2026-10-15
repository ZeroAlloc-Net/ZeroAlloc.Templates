namespace MyApp.Application;

/// <summary>
/// Name of the application's OpenTelemetry <c>ActivitySource</c> and <c>Meter</c>. Every
/// <c>[Instrument]</c> interface in this solution passes it, and Program.cs subscribes to it
/// with <c>AddSource(Telemetry.SourceName)</c> and <c>AddMeter(Telemetry.SourceName)</c>, so
/// renaming it here renames the subscription too.
/// </summary>
public static class Telemetry
{
    /// <summary>OTel source and meter name for the spans and metrics this app emits.</summary>
    public const string SourceName = "MyApp";
}
