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

        group.MapPost("/", async (OrderRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var command = OrderRequestToCommand.Map(req);
            var result = await mediator.Send(command, ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/orders/{result.Value.Value}", new CreatedOrderResponse(result.Value))
                : Results.Problem(result.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization("OrdersWrite");

        // Route binding stays `int` (per project ADR — keeps the HTTP shape stable
        // and avoids requiring the typed ID to implement IParsable<T>). The
        // endpoint wraps it into the OrderId typed ID at the API boundary, before
        // it crosses into the application layer.
        group.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetOrderByIdQuery(new OrderId(id)), ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Ok(ReadModelToResponse.Map(result.Value))
                : Results.NotFound();
        }).RequireAuthorization("OrdersRead");

        return app;
    }
}
