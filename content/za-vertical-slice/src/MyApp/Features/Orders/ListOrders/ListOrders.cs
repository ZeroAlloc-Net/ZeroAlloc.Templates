using System.Data;
using System.Data.Async;
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
        var items = await ListOrdersAsync(query.PageSize, skip, ct).ConfigureAwait(false);

        return Result<OrderPage, Error>.Success(new OrderPage(query.Page, query.PageSize, total, items));
    }

    [Query("SELECT COUNT(*) FROM \"Orders\"")]
    public partial Task<int> CountOrdersAsync(CancellationToken ct);

    // ZA.ORM 1.1.0's generator does not yet emit list-returning [Query] partials
    // (TODO marker in OrmGenerator.g.cs). Hand-roll the read until v1.2 lands.
    // The query uses the IAsyncDbConnection ctor-injected as `conn`; opening
    // here is idempotent — connection lifetime is owned by the DI scope.
    private async Task<IReadOnlyList<OrderListItem>> ListOrdersAsync(int limit, int offset, CancellationToken ct)
    {
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere)
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT \"Id\", \"CustomerId\", \"Total\" FROM \"Orders\" ORDER BY \"Id\" LIMIT @limit OFFSET @offset";

            var limitParam = cmd.CreateParameter();
            limitParam.ParameterName = "@limit";
            limitParam.Value = limit;
            cmd.Parameters.Add(limitParam);

            var offsetParam = cmd.CreateParameter();
            offsetParam.ParameterName = "@offset";
            offsetParam.Value = offset;
            cmd.Parameters.Add(offsetParam);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var items = new List<OrderListItem>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                var customerId = reader.GetInt32(1);
                var total = reader.GetDecimal(2);
                items.Add(new OrderListItem(new OrderId(id), new CustomerId(customerId), total));
            }
            return items;
        }
        finally
        {
            if (openedHere)
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }
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
