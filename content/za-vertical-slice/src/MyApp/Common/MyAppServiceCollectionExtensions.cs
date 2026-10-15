using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Telemetry;
using ZeroAlloc.Mediator.Validation;

namespace MyApp;

public static class MyAppServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZeroAlloc.Mediator pipeline: validation, authorization and telemetry. The
    /// generated <c>AddMediator()</c> registers <c>IMediator</c> and every slice
    /// handler in this assembly at compile time — no assembly scan, no reflection,
    /// so it is NativeAOT-safe. A new slice needs no line here.
    /// </summary>
    public static IServiceCollection AddMyApp(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        services.AddMediator()
                .WithValidation()
                .WithAuthorization(configureAuthorization)
                // Referencing ZeroAlloc.Mediator.Telemetry is what puts its TelemetryBehavior
                // in every request pipeline; WithTelemetry() is the documented fluent marker
                // and registers nothing. Program.cs subscribes OpenTelemetry to its meter.
                .WithTelemetry();

        // The validation behavior resolves ValidatorFor<TRequest> from DI and skips
        // requests that have none, so every [Validate] validator must be registered.
        services.AddZeroAllocValidators();

        // ZA.Inject-generated registry: emits AddScoped<T>() for every class in this
        // assembly marked [Scoped]. For handlers this is the same concrete-type
        // registration AddMediator() made, so the [Scoped] attribute is what sets
        // the handler lifetime.
        services.AddMyAppServices();

        return services;
    }
}
