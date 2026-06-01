using System.Data.Async;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
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

public sealed record OrderListItem(OrderId Id, CustomerId CustomerId, decimal Total);

public sealed record OrderPage(int Page, int PageSize, int Total, IReadOnlyList<OrderListItem> Items);

public sealed partial class ListOrdersHandler(IAsyncDbConnection conn)
    : IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>
{
    public async ValueTask<Result<OrderPage, Error>> Handle(ListOrdersQuery query, CancellationToken ct)
    {
        var total = await CountOrdersAsync(ct).ConfigureAwait(false);
        var skip = (query.Page - 1) * query.PageSize;

        var items = new List<OrderListItem>(query.PageSize);
        await foreach (var row in ListOrdersAsync(query.PageSize, skip, ct).ConfigureAwait(false))
        {
            items.Add(new OrderListItem(new OrderId(row.Id), new CustomerId(row.CustomerId), row.Total));
        }

        return Result<OrderPage, Error>.Success(new OrderPage(query.Page, query.PageSize, total, items));
    }

    [Query("SELECT COUNT(*) FROM \"Orders\"")]
    public partial Task<int> CountOrdersAsync(CancellationToken ct);

    [Query("SELECT \"Id\", \"CustomerId\", \"Total\" FROM \"Orders\" ORDER BY \"Id\" LIMIT @limit OFFSET @offset")]
    public partial IAsyncEnumerable<OrderListRow> ListOrdersAsync(int limit, int offset, [EnumeratorCancellation] CancellationToken ct);

    public sealed record OrderListRow(int Id, int CustomerId, decimal Total);
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
