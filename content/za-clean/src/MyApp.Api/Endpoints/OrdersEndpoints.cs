using MyApp.Api.Dtos;
using MyApp.Api.Mappings;
using MyApp.Application.GetOrderById;
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
                ? Results.Created($"/orders/{result.Value.Value}", new { Id = result.Value.Value })
                : Results.Problem(result.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization("OrdersWrite");

        group.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetOrderByIdQuery(id), ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Ok(OrderToResponse.Map(result.Value))
                : Results.NotFound();
        }).RequireAuthorization("OrdersRead");

        return app;
    }
}
