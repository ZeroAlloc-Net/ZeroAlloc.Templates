using System.Data.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Orders.CancelOrder;

#pragma warning disable MA0048 // Vertical-slice convention: request, handler, endpoint co-located.

/// <summary>
/// Soft-cancel an existing order. Three outcomes:
/// <list type="bullet">
///   <item><description>Order exists in <see cref="OrderStatus.Pending"/> → cancelled, returns 204.</description></item>
///   <item><description>Order exists in <see cref="OrderStatus.Cancelled"/> → 409 Conflict.</description></item>
///   <item><description>Order not found → 404.</description></item>
/// </list>
/// </summary>
[Validate]
[RequirePolicy("OrdersWrite")]
public readonly record struct CancelOrderCommand(OrderId Id)
    : IRequest<UnitResult<Error>>;

public sealed partial class CancelOrderHandler(IAsyncDbConnection conn)
    : IRequestHandler<CancelOrderCommand, UnitResult<Error>>
{
    public async ValueTask<UnitResult<Error>> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var status = await ReadStatusAsync(cmd.Id.Value, ct).ConfigureAwait(false);
        if (status is null)
        {
            return UnitResult<Error>.Failure(Error.NotFound(
                "order.not_found",
                $"Order {cmd.Id.Value} not found"));
        }

        if (string.Equals(status, nameof(OrderStatus.Cancelled), StringComparison.Ordinal))
        {
            return UnitResult<Error>.Failure(Error.Conflict(
                "order.already_cancelled",
                $"Order {cmd.Id.Value} is already cancelled"));
        }

        await CancelOrderAsync(cmd.Id.Value, nameof(OrderStatus.Cancelled), ct).ConfigureAwait(false);
        return UnitResult<Error>.Success();
    }

    [Query("SELECT \"Status\" FROM \"Orders\" WHERE \"Id\" = @id")]
    public partial Task<string?> ReadStatusAsync(int id, CancellationToken ct);

    [Command("UPDATE \"Orders\" SET \"Status\" = @status WHERE \"Id\" = @id")]
    public partial Task<int> CancelOrderAsync(int id, string status, CancellationToken ct);
}

public static class CancelOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/orders/{id:int}", static async (int id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new CancelOrderCommand(new OrderId(id)), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.NoContent()
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}
