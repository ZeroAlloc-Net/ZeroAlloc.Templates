using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using Xunit;

namespace MyApp.IntegrationTests.Features.Customers.CreateCustomer;

/// <summary>
/// Pins the ZA.Telemetry wiring: <c>[Instrument]</c> on <see cref="ICustomerWriter"/> generates
/// <c>CustomerWriterInstrumented</c>, <c>AddMyApp</c> registers it in front of
/// <see cref="CustomerWriter"/>, and a <c>POST /customers</c> through the hosted app emits the
/// <c>customer.insert</c> span and one <c>customers.created</c> count.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class CustomerWriterTelemetryTests : IClassFixture<MyAppFactory>, IDisposable
{
    private readonly MyAppFactory _factory;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(string Instrument, long Value)> _counts = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public CustomerWriterTelemetryTests(MyAppFactory factory)
    {
        _factory = factory;

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
        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => _counts.Enqueue((instrument.Name, value)));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public void Registered_customer_writer_is_the_generated_proxy()
    {
        using var scope = _factory.Services.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<ICustomerWriter>();

        Assert.IsType<CustomerWriterInstrumented>(writer);
    }

    [Fact]
    public async Task POST_customers_emits_insert_span_and_created_count()
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
        Assert.Equal("customer.insert", span.OperationName);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);

        var count = Assert.Single(_counts);
        Assert.Equal(("customers.created", 1L), count);
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
