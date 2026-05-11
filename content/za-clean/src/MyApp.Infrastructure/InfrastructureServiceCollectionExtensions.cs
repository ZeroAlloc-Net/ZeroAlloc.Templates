using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Infrastructure.External;
using MyApp.Infrastructure.Persistence;
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
    public static IServiceCollection AddMyAppInfrastructure(
        this IServiceCollection services,
        string sqliteConnectionString,
        string shippingBaseUrl)
    {
        services.AddDbContext<AppDbContext>(opt =>
        {
            opt.UseSqlite(sqliteConnectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
            // Owned-type snapshot diff produces a spurious "pending changes" warning on EF 9
            // against the existing InitialCreate migration. Tolerated for the template; a real
            // app should regenerate the migration when the snapshot legitimately drifts.
            opt.ConfigureWarnings(w =>
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
