using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Infrastructure.External;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Pins the ZA.Telemetry wiring: <c>[Instrument]</c> on <see cref="IShippingQuoteClient"/>
/// generates <c>ShippingQuoteClientInstrumented</c>, <c>AddMyAppInfrastructure</c> registers it
/// in front of the real <see cref="ShippingQuoteClient"/>, and a quote through the resolved
/// client emits the <c>shipping.quote</c> span and the <c>shipping.quote.duration_ms</c>
/// histogram. Only the HTTP leaf is faked, so the test runs offline.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class ShippingTelemetryTests : IDisposable
{
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(string Instrument, double Value)> _histogram = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public ShippingTelemetryTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Telemetry.SourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => _histogram.Enqueue((instrument.Name, value)));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public async Task Registered_shipping_client_is_the_generated_proxy_and_emits_span_and_histogram()
    {
        var services = new ServiceCollection();
        services.AddMyAppInfrastructure("Sqlite", "Data Source=:memory:", "https://shipping.test/");

        // Swap only the generated HTTP client for a canned response; the adapter and the
        // telemetry proxy around it are the production registrations.
        services.RemoveAll<IShippingQuoteHttpClient>();
        services.AddScoped<IShippingQuoteHttpClient, CannedShippingHttpClient>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IShippingQuoteClient>();

        Assert.IsType<ShippingQuoteClientInstrumented>(client);

        var quote = await client.GetQuoteAsync("1011AA", CancellationToken.None);

        Assert.True(quote.IsSuccess);
        Assert.Equal(7.5m, quote.Value.Amount);

        var span = Assert.Single(_activities);
        Assert.Equal("shipping.quote", span.OperationName);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);

        var sample = Assert.Single(_histogram);
        Assert.Equal("shipping.quote.duration_ms", sample.Instrument);
        Assert.True(sample.Value >= 0);
    }

    private sealed class CannedShippingHttpClient : IShippingQuoteHttpClient
    {
        public Task<ShippingQuoteResponse> GetAsync(string zip, CancellationToken ct = default)
            => Task.FromResult(new ShippingQuoteResponse(7.5m, "EUR"));
    }
}

/// <summary>
/// The activity and meter listeners are process-wide, so tests that assert exact span and
/// metric counts run on their own instead of in parallel with the rest of the suite.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry";
}
