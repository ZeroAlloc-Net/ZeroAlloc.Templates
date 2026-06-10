using MyApp.Domain.Orders.Events;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.EventSourcing.Aggregates;

#pragma warning disable MA0048 // OrderStatus enum co-located with OrderState by design

namespace MyApp.Domain.Orders;

/// <summary>Order lifecycle states — must match the [Transition] table on <see cref="OrderFsm"/>.</summary>
public enum OrderStatus
{
    Draft,
    Placed,
    Shipped,
    Cancelled,
}

/// <summary>
/// Immutable per-aggregate state struct. The <see cref="IAggregateState{TSelf}.Initial"/>
/// static seeds a Draft order with no customer; <see cref="Apply"/> projects each
/// committed/raised event into a new <see cref="OrderState"/> via a record-like
/// <c>with</c> expression so allocations stay on the stack.
/// </summary>
public partial struct OrderState : IAggregateState<OrderState>
{
    public static OrderState Initial => default;

    public OrderStatus Status { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public decimal Total { get; private set; }

    public string Currency { get; private set; }

    internal OrderState Apply(OrderPlaced e) => this with
    {
        Status = OrderStatus.Placed,
        CustomerId = e.CustomerId,
        Total = e.Total,
        Currency = e.Currency,
    };
}
