using MyApp.Features.Customers.CreateCustomer;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;

namespace MyApp;

public static class MyAppServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZeroAlloc.Mediator pipeline, validation, authorization and the
    /// ZA.Telemetry-instrumented <see cref="ICustomerWriter"/>. The
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
                .WithAuthorization(configureAuthorization);

        // The validation behavior resolves ValidatorFor<TRequest> from DI and skips
        // requests that have none, so every [Validate] validator must be registered.
        services.AddZeroAllocValidators();

        // ICustomerWriter resolves to the ZA.Telemetry proxy generated from the [Instrument]
        // attribute on the interface, wrapping the ZA.ORM CustomerWriter. The ZA.Inject
        // registrations below use TryAdd, so registering the interface first makes them skip
        // their own ICustomerWriter line and register only the concrete CustomerWriter.
        services.AddScoped<ICustomerWriter>(sp =>
            new CustomerWriterInstrumented(sp.GetRequiredService<CustomerWriter>()));

        // ZA.Inject-generated registry: emits AddScoped<T>() for every class in this
        // assembly marked [Scoped]. For handlers this is the same concrete-type
        // registration AddMediator() made, so the [Scoped] attribute is what sets
        // the handler lifetime.
        services.AddMyAppServices();

        return services;
    }
}
