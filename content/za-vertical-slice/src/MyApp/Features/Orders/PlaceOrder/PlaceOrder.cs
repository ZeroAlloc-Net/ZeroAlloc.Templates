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
public readonly record struct PlaceOrderCommand(
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
/// </summary>
internal sealed class Order
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
    }

    public OrderId Id { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public decimal Total { get; private set; }
}
