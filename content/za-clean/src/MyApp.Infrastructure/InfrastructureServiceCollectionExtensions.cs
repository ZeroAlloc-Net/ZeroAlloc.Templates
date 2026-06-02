using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Infrastructure.External;
using Npgsql;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;

namespace MyApp.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure assembly. Wires up the ZA.ORM
/// <see cref="IAsyncDbConnection"/> per provider, the [Scoped] services emitted
/// by ZA.Inject, and the ZA.Rest typed HTTP client wrapped in the ZA.Resilience
/// proxy.
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
                sp.GetRequiredService<ZeroAlloc.Resilience.RetryPolicy>(),
                sp.GetRequiredService<ZeroAlloc.Resilience.TimeoutPolicy>()),
            opts =>
            {
                opts.BaseAddress = new Uri(shippingBaseUrl);
                // IRestSerializer must be registered; otherwise ShippingQuoteHttpClientClient
                // can't be activated by AddRestResilience's factory at request time.
                opts.UseSerializer<SystemTextJsonSerializer>();
            });

        // Register the resilience policies as singletons; AddRestResilience doesn't
        // register them (only the per-interface AddXxxResilience<TImpl>() generator
        // extension does), so add them explicitly here. Values mirror the [Retry] and
        // [Timeout] attributes on IShippingQuoteHttpClient.
        services.AddSingleton(new ZeroAlloc.Resilience.RetryPolicy(maxAttempts: 3, backoffMs: 200, jitter: true, perAttemptTimeoutMs: 0));
        services.AddSingleton(new ZeroAlloc.Resilience.TimeoutPolicy(5_000));

        return services;
    }
}
