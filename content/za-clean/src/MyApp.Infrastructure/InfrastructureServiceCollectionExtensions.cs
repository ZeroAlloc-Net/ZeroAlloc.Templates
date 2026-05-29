using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Infrastructure.External;
using MyApp.Infrastructure.Persistence;
using MyApp.Infrastructure.Persistence.CompiledModel;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;

namespace MyApp.Infrastructure;

/// <summary>
/// Composition entry point for the Infrastructure assembly. Wires up the EF Core
/// <see cref="AppDbContext"/>, the [Scoped] services emitted by ZA.Inject, and the
/// ZA.Rest typed HTTP client wrapped in the ZA.Resilience proxy.
///
/// The Rest+Resilience bridge needs direct access to the generator-emitted internal
/// proxy class (<c>IShippingQuoteHttpClientResilienceProxy</c>) — composing the
/// pipeline here keeps that internal type out of the Api project's surface.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Wires the EF Core <see cref="AppDbContext"/>, ZA.Inject-emitted services, and
    /// the ZA.Rest + ZA.Resilience composition into the host services container.
    /// </summary>
    /// <param name="provider">Database provider selector. <c>"Postgres"</c> (case-insensitive)
    /// dispatches to <c>UseNpgsql</c>; any other value falls through to <c>UseSqlite</c>.</param>
    /// <param name="connectionString">Provider-specific connection string. Sqlite: <c>Data Source=...</c>.
    /// Postgres: <c>Host=...;Port=...;Username=...;Password=...;Database=...</c>.</param>
    /// <param name="shippingBaseUrl">Base URL for the ZA.Rest typed shipping client.</param>
    public static IServiceCollection AddMyAppInfrastructure(
        this IServiceCollection services,
        string provider,
        string connectionString,
        string shippingBaseUrl)
    {
        services.AddDbContextPool<AppDbContext>(opts =>
        {
            if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                opts.UseNpgsql(connectionString, npg =>
                    npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                // No UseModel for Postgres. The compiled model in
                // Persistence/CompiledModel/ was generated via
                // `dotnet ef dbcontext optimize` against the Sqlite provider —
                // its type-mapping internals come from
                // Microsoft.EntityFrameworkCore.Sqlite.Storage.Internal, so
                // applying it to an Npgsql DbContext produces malformed SQL
                // (e.g. 42601 "syntax error at or near $" on SELECT…WHERE
                // queries). Production Postgres adopters wanting AOT publish
                // should regenerate the compiled model against Npgsql:
                //   dotnet ef dbcontext optimize \
                //     --output-dir Persistence/CompiledModel.Postgres \
                //     -- --provider Postgres
                // then dispatch the right UseModel call from this branch.
            }
            else
            {
                opts.UseSqlite(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                // Compiled model required for AOT publish; bypasses the reflection-based
                // design-time model pipeline. Regenerate via
                // `dotnet ef dbcontext optimize --output-dir Persistence/CompiledModel`
                // after any entity/mapping change.
                opts.UseModel(AppDbContextModel.Instance);
            }
            // Owned-type snapshot diff produces a spurious "pending changes" warning on EF 9
            // against the existing InitialCreate migration. Tolerated for the template; a real
            // app should regenerate the migration when the snapshot legitimately drifts.
            opts.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
