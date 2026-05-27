using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Orders.GetOrder;

#pragma warning disable MA0048 // Vertical-slice convention: request, DTO, handler, endpoint co-located.

/// <summary>
/// Retrieve a single order by id. <c>[RequirePolicy("OrdersRead")]</c> +
/// <c>RequireAuthorization("OrdersRead")</c> enforce the read-scope at both the
/// mediator and endpoint layers.
/// </summary>
[Validate]
[RequirePolicy("OrdersRead")]
public sealed record GetOrderQuery([property: GreaterThan(0)] int Id)
    : IRequest<Result<OrderDto, Error>>;

/// <summary>
/// Wire-format projection of an <see cref="Order"/>. Primitive properties so the
/// JSON response stays flat — <c>{ "id": 1, "customerId": 42, "total": 99.99 }</c>.
/// A larger projection (e.g. nested order lines, enum-to-string status) is the
/// canonical use case for <c>[Map&lt;Order, OrderDto&gt;]</c> from
/// <c>ZeroAlloc.Mapping</c>; the trivial three-field shape here is hand-projected
/// inline for readability.
/// </summary>
public sealed record OrderDto(int Id, int CustomerId, decimal Total);

public sealed class GetOrderHandler(AppDbContext db)
    : IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>
{
    public async ValueTask<Result<OrderDto, Error>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == new OrderId(query.Id), ct)
            .ConfigureAwait(false);

        return order is null
            ? Result<OrderDto, Error>.Failure(Error.NotFound(
                "order.not_found",
                $"Order {query.Id} not found"))
            : Result<OrderDto, Error>.Success(new OrderDto(
                order.Id.Value,
                order.CustomerId.Value,
                order.Total));
    }
}

public static class GetOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{id:int}", static async (int id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetOrderQuery(id), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersRead");
}
