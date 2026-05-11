using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.CreateOrder;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application;

/// <summary>
/// Composition entry point for the Application assembly. Wires up the ZA.Mediator
/// dispatcher (with its <see cref="ZeroAlloc.Mediator.IMediator"/> partial interface
/// generated for this assembly's handlers) and the [Scoped] handlers/services
/// emitted by ZA.Inject's per-assembly extension.
///
/// The generated <c>AddMediator()</c> extension is <c>internal</c> per-assembly — by
/// exposing this public wrapper, callers in the Api project don't need to share
/// internals just to register the mediator.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMyAppApplication(this IServiceCollection services)
    {
        // Manual handler registration. ZA.Mediator's RegisterHandlersFromAssembly
        // uses reflection-based assembly scanning, which is incompatible with
        // NativeAOT (RequiresDynamicCode/RequiresUnreferencedCode). Each handler
        // is wired explicitly here so the DI container resolves the closed
        // generic IRequestHandler<TReq,TResp> directly — no reflection.
        services.AddMediator();
        services.AddScoped<IRequestHandler<CreateOrderCommand, Result<OrderId, ApplicationError>>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderByIdQuery, Result<Order, ApplicationError>>, GetOrderByIdHandler>();
        services.AddMyAppApplicationServices();
        return services;
    }
}
