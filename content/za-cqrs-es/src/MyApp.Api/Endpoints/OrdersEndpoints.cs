using Microsoft.AspNetCore.OutputCaching;
using MyApp.Api.Dtos;
using MyApp.Application;
using MyApp.Application.Orders.PlaceOrder;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", async (PlaceOrderRequest req, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
        {
            var cmd = new PlaceOrderCommand(new CustomerId(req.CustomerId), req.Total, req.Currency);
            var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // Conservative tag-based eviction — future Task adds GET /orders/{id}
                // that reads the cached projection row; writes invalidate all of them.
                await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);
                return Results.Created(
                    $"/orders/{result.Value.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture)}",
                    new CreatedOrderResponse(result.Value));
            }
            return Results.Problem(result.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization("OrdersWrite");

        return app;
    }
}
