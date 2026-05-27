using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
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
public sealed record PlaceOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: GreaterThan(0)] decimal Total)
    : IRequest<Result<OrderId, Error>>;

/// <summary>
/// Persists the order and returns its newly-assigned <see cref="OrderId"/>.
/// </summary>
public sealed class PlaceOrderHandler(AppDbContext db)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(new CustomerId(cmd.CustomerId), cmd.Total);
        await db.Orders.AddAsync(order, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<OrderId, Error>.Success(order.Id);
    }
}

/// <summary>
/// Minimal-API endpoint mapping. Picked up by the assembly walk in Program.cs
/// because the type is a public static class ending in "Endpoint" with a
/// matching <c>Map(IEndpointRouteBuilder)</c> method.
/// </summary>
public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/orders/{result.Value.Value}", result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}

/// <summary>
/// Persistence entity owned by this slice. EF Core assigns <see cref="Id"/> on
/// INSERT via the <see cref="OrderId"/> value-converter configured in
/// <see cref="AppDbContext.OnModelCreating"/>; constructing with an
/// <see cref="OrderId"/> wrapping <c>0</c> signals "let the database pick an id".
/// <para>
/// <b>Public on purpose:</b> read-side slices (GetOrder, ListOrders) project from
/// this entity. Handlers, validators, and DTOs stay internal to their slice; only
/// the persistence entity crosses the slice boundary. The ConventionTests
/// enforce this distinction.
/// </para>
/// </summary>
public sealed class Order
{
    // EF materialisation constructor.
    private Order()
    {
    }

    public Order(CustomerId customerId, decimal total)
    {
        Id = new OrderId(0);
        CustomerId = customerId;
        Total = total;
        Status = OrderStatus.Pending;
    }

    public OrderId Id { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public decimal Total { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Soft-cancels the order. Returns <c>false</c> if the order is already
    /// cancelled (idempotency signal — CancelOrder slice maps this to
    /// HTTP 409 Conflict).
    /// </summary>
    public bool Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        Status = OrderStatus.Cancelled;
        return true;
    }
}

/// <summary>
/// Order lifecycle. New orders start in <see cref="Pending"/>; the only
/// transition is to <see cref="Cancelled"/> via <see cref="Order.Cancel"/>.
/// Persisted as a string column (see <c>AppDbContext.OnModelCreating</c>).
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Cancelled = 1,
}
