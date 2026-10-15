using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApp.Common;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Pins the template's telemetry design: cross-cutting telemetry comes from the mediator
/// pipeline, so no slice carries telemetry code. One <c>POST /customers</c> through the hosted
/// app dispatches one <c>Send</c>, which must emit one <c>mediator.send</c> span on the
/// <see cref="Telemetry.MediatorSourceName"/> activity source, opened by the generated
/// <c>Send</c>, and add 1 to the <c>mediator.requests_total</c> counter on the meter of the same
/// name, recorded by ZeroAlloc.Mediator.Telemetry's pipeline behavior. A green request carries
/// no <c>error.type</c> tag on either.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class MediatorTelemetryTests : IClassFixture<MyAppFactory>, IDisposable
{
    private readonly MyAppFactory _factory;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, int TagCount)> _counts = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public MediatorTelemetryTests(MyAppFactory factory)
    {
        _factory = factory;

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.MediatorSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Telemetry.MediatorSourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => _counts.Enqueue((instrument.Name, value, tags.Length)));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public async Task One_request_emits_one_mediator_send_span_and_one_requests_total_count()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.write"]));

        var resp = await client.PostAsJsonAsync("/customers", new
        {
            name = "Telemetry Ltd.",
            email = "ops@telemetry.example",
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var span = Assert.Single(_activities);
        Assert.Equal("mediator.send", span.OperationName);
        Assert.Equal("CreateCustomerCommand", span.GetTagItem("request.type"));
        Assert.Null(span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);

        var count = Assert.Single(_counts);
        Assert.Equal(("mediator.requests_total", 1L, 0), count);
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
