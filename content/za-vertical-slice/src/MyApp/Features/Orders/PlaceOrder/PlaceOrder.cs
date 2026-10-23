using System.Data.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Orders.PlaceOrder;

#pragma warning disable MA0048 // Vertical-slice convention: request, validator, handler, endpoint, entity co-located.

/// <summary>
/// Place a new order for a customer. The <c>[Validate]</c> attribute drives the
/// ZA.Validation generator to emit <see cref="PlaceOrderCommandValidator"/>,
/// which the ZA.Mediator.Validation pipeline behaviour invokes before the handler
/// runs. <c>[RequirePolicy("OrdersWrite")]</c> enforces the same scope at the
/// mediator layer that <c>RequireAuthorization("OrdersWrite")</c> enforces at the
/// endpoint layer — defense in depth.
/// </summary>
[Validate]
[RequirePolicy("OrdersWrite")]
public readonly record struct PlaceOrderCommand(
    CustomerId CustomerId,
    [property: GreaterThan(0)] decimal Total)
    : IRequest<Result<OrderId, Error>>;

/// <summary>
/// Persists the order and returns its newly-assigned <see cref="OrderId"/>.
/// </summary>
[Scoped]
public sealed partial class PlaceOrderHandler(IAsyncDbConnection conn)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        // Validator (`[GreaterThan(0)]` on Total) guarantees cmd.Total > 0,
        // so Money.TryCreate cannot fail here. Money is internal-only — the
        // wire surface (PlaceOrderCommand.Total) stays bare decimal.
        var money = Money.TryCreate(cmd.Total, "EUR").Value;
        var id = await InsertOrderAsync(
            cmd.CustomerId.Value,
            MoneyConverter.ToStorage(money),
            nameof(OrderStatus.Pending),
            ct).ConfigureAwait(false);
        return Result<OrderId, Error>.Success(new OrderId(id));
    }

    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertOrderAsync(int customerId, string total, string status, CancellationToken ct);
}

/// <summary>
/// Minimal-API endpoint mapping. Program.cs wires it with an explicit
/// <c>PlaceOrderEndpoint.Map(app)</c> call, one per slice.
/// </summary>
public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        // Evicts nothing: only 200 responses are cached, so no cached GET can
        // describe an id that did not exist yet.
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    return Results.Created($"/orders/{result.Value.Value}", result.Value);
                }

                return result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}

public enum OrderStatus
{
    Pending = 0,
    Cancelled = 1,
}
