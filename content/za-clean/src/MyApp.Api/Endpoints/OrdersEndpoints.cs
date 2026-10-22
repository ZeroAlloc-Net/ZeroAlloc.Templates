using System.Globalization;
using Microsoft.AspNetCore.OutputCaching;
using MyApp.Api.Dtos;
using MyApp.Api.Mappings;
using MyApp.Application.GetOrderById;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;

namespace MyApp.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        // Creating an order evicts nothing. Only 200 responses are cached, so no
        // cached /orders/{id} can describe an id that did not exist yet. An
        // endpoint that changes an existing order evicts that order alone, with
        // EvictByTagAsync("order:" + id); evicting the whole "orders" tag on every
        // write empties the cache under a steady write rate.
        group.MapPost("/", async (OrderRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var command = OrderRequestToCommand.Map(req);
            var result = await mediator.Send(command, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return Results.Created($"/orders/{result.Value.Value}", new CreatedOrderResponse(result.Value));
            }
            return Results.Problem(result.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization("OrdersWrite");

        // Route binding stays `int` (per project ADR — keeps the HTTP shape stable
        // and avoids requiring the typed ID to implement IParsable<T>). The
        // endpoint wraps it into the OrderId typed ID at the API boundary, before
        // it crosses into the application layer.
        group.MapGet("/{id:int}", async (int id, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetOrderByIdQuery(new OrderId(id)), ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Results.NotFound();

            // Tag the cached response with its id, so a write to this order can evict it
            // alone. This handler runs only on a cache miss; a hit never pays for the tag.
            http.Features.Get<IOutputCacheFeature>()?.Context.Tags.Add(
                "order:" + id.ToString(CultureInfo.InvariantCulture));
            return Results.Ok(ReadModelToResponse.Map(result.Value));
        }).RequireAuthorization("OrdersRead").CacheOutput("OrderById");

        return app;
    }
}
