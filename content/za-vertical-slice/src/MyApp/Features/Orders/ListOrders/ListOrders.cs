using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Orders.ListOrders;

#pragma warning disable MA0048 // Vertical-slice convention: request, DTO, handler, endpoint co-located.

/// <summary>
/// Paginated list of orders. Page is 1-based; page size is capped to 100 to
/// prevent unbounded queries. The slice owns its own list-item shape
/// (<see cref="OrderListItem"/>) rather than reusing GetOrder's <c>OrderDto</c>
/// — vertical-slice convention is that each slice picks the projection that
/// fits its endpoint, and shapes are free to diverge slice-by-slice.
/// </summary>
[Validate]
[RequirePolicy("OrdersRead")]
public readonly record struct ListOrdersQuery(
    [property: GreaterThan(0)] int Page,
    [property: InclusiveBetween(1, 100)] int PageSize)
    : IRequest<Result<OrderPage, Error>>;

public sealed record OrderListItem(int Id, int CustomerId, decimal Total);

public sealed record OrderPage(int Page, int PageSize, int Total, IReadOnlyList<OrderListItem> Items);

public sealed class ListOrdersHandler(AppDbContext db)
    : IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>
{
    public async ValueTask<Result<OrderPage, Error>> Handle(ListOrdersQuery query, CancellationToken ct)
    {
        var total = await db.Orders.CountAsync(ct).ConfigureAwait(false);

        var items = await db.Orders
            .AsNoTracking()
            .OrderBy(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new OrderListItem(o.Id.Value, o.CustomerId.Value, o.Total))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result<OrderPage, Error>.Success(new OrderPage(query.Page, query.PageSize, total, items));
    }
}

public static class ListOrdersEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders", static async (IMediator mediator, CancellationToken ct, int page = 1, int pageSize = 20) =>
            {
                var result = await mediator.Send(new ListOrdersQuery(page, pageSize), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersRead");
}
