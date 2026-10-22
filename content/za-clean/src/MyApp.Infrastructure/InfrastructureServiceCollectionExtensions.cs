using System.Data.Async;
using System.Data.Common;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Infrastructure.External;
using Npgsql;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;

namespace MyApp.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure assembly. Wires up the ZA.ORM
/// <see cref="IAsyncDbConnection"/> per provider, the [Scoped] services emitted
/// by ZA.Inject, the ZA.Rest typed HTTP client wrapped in the ZA.Resilience
/// proxy, and the ZA.Telemetry proxy around the shipping-quote adapter.
///
/// The Rest+Resilience bridge needs direct access to the generator-emitted internal
/// proxy class (<c>IShippingQuoteHttpClientResilienceProxy</c>) — composing the
/// pipeline here keeps that internal type out of the Api project's surface.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZA.ORM <see cref="IAsyncDbConnection"/>, ZA.Inject-emitted services,
    /// and the ZA.Rest + ZA.Resilience composition into the host services container.
    /// </summary>
    /// <param name="provider">Database provider selector. <c>"Postgres"</c> (case-insensitive)
    /// dispatches to <c>NpgsqlConnection</c>; any other value falls through to <c>SqliteConnection</c>.</param>
    /// <param name="connectionString">Provider-specific connection string. Sqlite: <c>Data Source=...</c>.
    /// Postgres: <c>Host=...;Port=...;Username=...;Password=...;Database=...</c>.</param>
    /// <param name="shippingBaseUrl">Base URL for the ZA.Rest typed shipping client.</param>
    public static IServiceCollection AddMyAppInfrastructure(
        this IServiceCollection services,
        string provider,
        string connectionString,
        string shippingBaseUrl)
    {
        var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);
        if (isPostgres)
        {
            connectionString = WithThroughputDefaults(connectionString);
        }

        // IAsyncDbConnection registered scoped — ZA.ORM-generated repositories
        // pull a fresh connection per HTTP request and the generator-emitted
        // ref-counted open/close pipeline runs against it.
        services.AddScoped<IAsyncDbConnection>(_ =>
        {
            if (isPostgres)
            {
                return new NpgsqlConnection(connectionString).AsAsync();
            }
            return new SqliteConnection(connectionString).AsAsync();
        });

        // IShippingQuoteClient resolves to the ZA.Telemetry proxy generated from the [Instrument]
        // attribute on the interface, wrapping the real ShippingQuoteClient. ZA.Inject's generated
        // registrations use TryAdd, so registering the interface first makes the call below skip
        // its own IShippingQuoteClient line and register only the concrete ShippingQuoteClient
        // the proxy wraps.
        services.AddScoped<IShippingQuoteClient>(sp =>
            new ShippingQuoteClientInstrumented(sp.GetRequiredService<ShippingQuoteClient>()));

        // [Scoped]/[Singleton]/[Transient] registrations emitted by ZA.Inject for this assembly.
        services.AddMyAppInfrastructureServices();

        // Compose the Rest-generated client (ShippingQuoteHttpClientClient) with the
        // Resilience-generated proxy (IShippingQuoteHttpClientResilienceProxy). The proxy
        // is internal to this assembly, hence the wiring lives here.
        services.AddRestResilience<
            IShippingQuoteHttpClient,
            ShippingQuoteHttpClientClient,
            IShippingQuoteHttpClientResilienceProxy>(
            (inner, sp) => new IShippingQuoteHttpClientResilienceProxy(
                inner,
                sp.GetRequiredService<ShippingQuoteHttpClientResiliencePolicies>()),
            opts =>
            {
                opts.BaseAddress = new Uri(shippingBaseUrl);
                // IRestSerializer must be registered; otherwise ShippingQuoteHttpClientClient
                // can't be activated by AddRestResilience's factory at request time. The
                // source-generated context keeps serialization free of reflection for Native AOT.
                opts.UseSerializer(new SystemTextJsonSerializer(ShippingJsonContext.Default));
            });

        // The proxy's policy settings, generated from the [Retry] and [Timeout] attributes on
        // IShippingQuoteHttpClient. AddRestResilience registers IShippingQuoteHttpClient itself,
        // so only the policies are registered here, not the full AddShippingQuoteHttpClientResilience.
        // Pass a configure callback to change the values at startup, for example from IOptions.
        services.AddShippingQuoteHttpClientResiliencePolicies();

        return services;
    }

    /// <summary>
    /// Turns on two Npgsql settings that the connection string does not set itself. Both are
    /// off by default in Npgsql; see docs/benchmarks/2026-10-22-template-capacity.md for what
    /// each one is worth here.
    /// <list type="bullet">
    /// <item><c>Max Auto Prepare</c>: Npgsql prepares a statement once it has run a few times on a
    /// physical connection, so the server skips parse and plan on every later call. Every
    /// statement in this app repeats.</item>
    /// <item><c>Multiplexing</c>: commands from concurrent requests share physical connections and
    /// are written to the socket in batches, instead of each request holding a connection for
    /// its whole round trip. It needs the async Npgsql API throughout, which ZA.ORM uses, and it
    /// keeps no session state between commands: no <c>SET</c>, temporary tables or
    /// <c>LISTEN</c> outside a transaction. Add <c>Multiplexing=false</c> to the connection
    /// string if your code needs those.</item>
    /// </list>
    /// </summary>
    private static string WithThroughputDefaults(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // The normalised string lists only the keys that were set, under their canonical names,
        // so a synonym such as MaxAutoPrepare still counts as set.
        var explicitlySet = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString };
        if (!explicitlySet.ContainsKey("Max Auto Prepare"))
        {
            builder.MaxAutoPrepare = 20;
        }
        if (!explicitlySet.ContainsKey("Multiplexing"))
        {
            builder.Multiplexing = true;
        }
        return builder.ConnectionString;
    }
}
