using System;
using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Domain.Orders;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.EventStore;
using Npgsql;
using ZeroAlloc.EventSourcing;
using ZeroAlloc.EventSourcing.Aggregates;
using ZeroAlloc.EventSourcing.InMemory;

namespace MyApp.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure assembly. Wires up the ZA.ORM
/// <see cref="IAsyncDbConnection"/> (used by projection repositories), the
/// in-memory event store (Task 5 swaps this for a SQL adapter once the upstream
/// package ships), and the per-aggregate repositories.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMyAppInfrastructure(
        this IServiceCollection services,
        string provider,
        string connectionString)
    {
        var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);

        // IAsyncDbConnection — scoped per request. ZA.ORM-generated repositories
        // pull a fresh connection here.
        services.AddScoped<IAsyncDbConnection>(_ =>
        {
            if (isPostgres)
            {
                return new NpgsqlConnection(connectionString).AsAsync();
            }
            return new SqliteConnection(connectionString).AsAsync();
        });

        // [Scoped]/[Singleton]/[Transient] registrations emitted by ZA.Inject.
        services.AddMyAppInfrastructureServices();

        // ── Event store wiring ─────────────────────────────────────────────────
        // Custom event-type registry + AOT-friendly JSON serializer (System.Text.Json
        // with a source-generated JsonSerializerContext). Must be registered BEFORE
        // AddEventSourcing() so the default ZeroAllocEventSerializer is not picked
        // up.
        services.AddSingleton<IEventSerializer, MyAppEventSerializer>();
        services.AddSingleton<IEventTypeRegistry, MyAppEventTypeRegistry>();

        services.AddEventSourcing()
                .UseInMemoryEventStore()
                .UseAggregateRepository<Order, OrderId>(
                    factory: () => new Order(),
                    streamIdFactory: id => new StreamId("order-" + id.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture)));

        return services;
    }
}
