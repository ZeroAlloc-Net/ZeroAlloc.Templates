using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

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
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
        services.AddMyAppApplicationServices();
        return services;
    }
}
