using System;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Orders.PlaceOrder;
using MyApp.Application.Projections;
using MyApp.Domain.Orders.Events;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace MyApp.Application;

/// <summary>
/// Composition entry point for the Application assembly. Wires up the ZA.Mediator
/// dispatcher with per-request <c>[RequirePolicy]</c> enforcement plus explicit
/// handler/projection registrations. Mirrors za-clean's pattern: ZA.Mediator's
/// <c>RegisterHandlersFromAssembly</c> uses reflection-based scanning that is
/// incompatible with NativeAOT, so each <c>IRequestHandler&lt;,&gt;</c> and
/// <c>INotificationHandler&lt;T&gt;</c> is wired by hand.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMyAppApplication(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        ArgumentNullException.ThrowIfNull(configureAuthorization);

        services.AddMediator()
                .WithAuthorization(configureAuthorization);

        // Explicit handler registration — RegisterHandlersFromAssembly relies on
        // reflection scanning that is incompatible with NativeAOT. Register both
        // the IRequestHandler<,> interface and the concrete type because the
        // ZA.Mediator-generated dispatcher resolves handlers as concrete types
        // (see ZeroAlloc.Mediator.g.cs's GetRequiredService<TConcreteHandler>()).
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<IRequestHandler<PlaceOrderCommand, Result<OrderId, ApplicationError>>>(sp => sp.GetRequiredService<PlaceOrderHandler>());

        // Projection notification handler — picks up published OrderPlaced events.
        // Mediator-generated Publish<T>() resolves concrete types directly, so
        // register both the concrete class and the interface mapping.
        services.AddScoped<OrderListingsProjection>();
        services.AddScoped<INotificationHandler<OrderPlaced>>(sp => sp.GetRequiredService<OrderListingsProjection>());

        return services;
    }
}
