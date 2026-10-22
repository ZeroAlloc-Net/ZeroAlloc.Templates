using System.Data.Async;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
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
public readonly record struct GetOrderQuery(OrderId Id)
    : IRequest<Result<OrderDto, Error>>;

/// <summary>
/// Wire-format projection of an order. Carries the typed IDs directly — the
/// ZA.ValueObjects-generated JSON converter serialises each as a bare integer,
/// so the wire shape stays
/// <c>{ "id": 1, "customerId": 42, "total": 99.99 }</c>.
/// A larger projection (e.g. nested order lines, enum-to-string status) is the
/// canonical use case for <c>[Map&lt;Order, OrderDto&gt;]</c> from
/// <c>ZeroAlloc.Mapping</c>; the trivial three-field shape here is hand-projected
/// inline for readability.
/// </summary>
public sealed record OrderDto(OrderId Id, CustomerId CustomerId, decimal Total);

[Scoped]
public sealed partial class GetOrderHandler(IAsyncDbConnection conn)
    : IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>
{
    public async ValueTask<Result<OrderDto, Error>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var row = await ReadOrderAsync(query.Id.Value, ct).ConfigureAwait(false);
        return row is null
            ? Result<OrderDto, Error>.Failure(Error.NotFound(
                "order.not_found",
                $"Order {query.Id.Value} not found"))
            : Result<OrderDto, Error>.Success(new OrderDto(
                new OrderId(row.Id),
                new CustomerId(row.CustomerId),
                MoneyConverter.AmountFromStorage(row.Total)));
    }

    [Query("SELECT \"Id\", \"CustomerId\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id")]
    public partial Task<OrderRow?> ReadOrderAsync(int id, CancellationToken ct);

    // Total is stored as the MoneyConverter wire shape "<amount>|<currency>";
    // the handler parses only the amount back for the DTO.
    public sealed record OrderRow(int Id, int CustomerId, string Total);
}

public static class GetOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{id:int}", static async (int id, IMediator mediator, HttpContext http, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetOrderQuery(new OrderId(id)), ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                    return result.Error.ToProblem();

                // Tag a response that the output cache will store with its id, so a write to this
                // row can evict it alone. This handler runs only on a cache miss, so a hit never
                // pays for the tag, and a response that is not stored allocates nothing for it.
                if (http.Features.Get<IOutputCacheFeature>()?.Context is { EnableOutputCaching: true, AllowCacheStorage: true } cache)
                    cache.Tags.Add("order:" + id.ToString(CultureInfo.InvariantCulture));
                return Results.Ok(result.Value);
            })
            .RequireAuthorization("OrdersRead")
            .CacheOutput("OrderById");
}
