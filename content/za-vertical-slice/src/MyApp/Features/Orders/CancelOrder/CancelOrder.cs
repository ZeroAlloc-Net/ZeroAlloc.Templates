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
public sealed record CancelOrderCommand([property: GreaterThan(0)] int Id)
    : IRequest<UnitResult<Error>>;

public sealed class CancelOrderHandler(AppDbContext db)
    : IRequestHandler<CancelOrderCommand, UnitResult<Error>>
{
    public async ValueTask<UnitResult<Error>> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new OrderId(cmd.Id);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return UnitResult<Error>.Failure(Error.NotFound(
                "order.not_found",
                $"Order {cmd.Id} not found"));
        }

        if (!order.Cancel())
        {
            return UnitResult<Error>.Failure(Error.Conflict(
                "order.already_cancelled",
                $"Order {cmd.Id} is already cancelled"));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return UnitResult<Error>.Success();
    }
}

public static class CancelOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/orders/{id:int}", static async (int id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new CancelOrderCommand(id), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.NoContent()
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}
