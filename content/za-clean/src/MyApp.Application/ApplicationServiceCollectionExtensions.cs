using System;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.CreateOrder;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace MyApp.Application;

/// <summary>
/// Composition entry point for the Application assembly. Wires up the ZA.Mediator
/// dispatcher (with its <see cref="ZeroAlloc.Mediator.IMediator"/> partial interface
/// generated for this assembly's handlers), the per-request [RequirePolicy] enforcement
/// via ZA.Mediator.Authorization, and the [Scoped] handlers/services emitted by
/// ZA.Inject's per-assembly extension.
///
/// The generated <c>AddMediator()</c> extension is <c>internal</c> per-assembly — by
/// exposing this public wrapper, callers in the Api project don't need to share
/// internals just to register the mediator.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator pipeline and all application handlers.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configureAuthorization">
    /// Required: opt into a security-context source. The Api project passes
    /// <c>o => o.UseAccessor&lt;HttpSecurityContextAccessor&gt;()</c> to bridge
    /// <see cref="System.Security.Claims.ClaimsPrincipal"/> from HttpContext.User.
    /// Non-HTTP hosts (CLI, worker) can call <c>o.UseAnonymousSecurityContext()</c>
    /// to disable [RequirePolicy] enforcement, or supply their own accessor.
    /// </param>
    public static IServiceCollection AddMyAppApplication(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        ArgumentNullException.ThrowIfNull(configureAuthorization);

        // Manual handler registration. ZA.Mediator's RegisterHandlersFromAssembly
        // uses reflection-based assembly scanning, which is incompatible with
        // NativeAOT (RequiresDynamicCode/RequiresUnreferencedCode). Each handler
        // is wired explicitly here so the DI container resolves the closed
        // generic IRequestHandler<TReq,TResp> directly — no reflection.
        services.AddMediator()
                .WithAuthorization(configureAuthorization);
        services.AddScoped<IRequestHandler<CreateOrderCommand, Result<OrderId, ApplicationError>>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderByIdQuery, Result<OrderReadModel, ApplicationError>>, GetOrderByIdHandler>();
        services.AddMyAppApplicationServices();
        return services;
    }
}
