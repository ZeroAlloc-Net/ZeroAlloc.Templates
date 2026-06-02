using MyApp.Authorization;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Features.Orders.CancelOrder;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Results;

namespace MyApp;

public static class MyAppServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZeroAlloc.Mediator pipeline, validation, authorization, and
    /// hand-listed slice handlers. The hand-list pattern is the AOT-friendly
    /// alternative to <c>RegisterHandlersFromAssembly</c> (which uses
    /// reflection-based assembly scanning incompatible with NativeAOT).
    /// Each new slice adds one line to the block below.
    /// </summary>
    public static IServiceCollection AddMyApp(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        services.AddMediator()
                .WithValidation()
                .WithAuthorization(configureAuthorization);

        // Per-slice handler registrations — keep in declaration order matching
        // the Features/<Area>/<UseCase>/ folder tree so adding a slice is
        // visually obvious.
        services.AddScoped<IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>, CreateCustomerHandler>();
        services.AddScoped<IRequestHandler<GetCustomerQuery, Result<CustomerDto, Error>>, GetCustomerHandler>();
        services.AddScoped<IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>, PlaceOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>, GetOrderHandler>();
        services.AddScoped<IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>, ListOrdersHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCommand, UnitResult<Error>>, CancelOrderHandler>();

        // ZA.Inject-generated registry: emits AddScoped<THandler>() for every
        // class in this assembly marked with [Scoped]. Mediator's source-generated
        // dispatch resolves handlers by their concrete type, so this concrete
        // registration is what makes the dispatch actually work — the
        // IRequestHandler<,> registrations above remain for any code that
        // resolves handlers via the interface (rare but harmless).
        services.AddMyAppServices();

        return services;
    }
}
