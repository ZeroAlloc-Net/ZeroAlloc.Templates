using System;
using MyApp.Domain.Orders.Events;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.EventSourcing.Aggregates;

namespace MyApp.Domain.Orders;

/// <summary>
/// Order aggregate root. State is captured in <see cref="OrderState"/>; command
/// methods validate the current status via <see cref="OrderFsm"/> and then raise
/// the appropriate event. The base class' source generator emits the
/// <c>ApplyEvent</c> switch over partial declarations — keep <see cref="Order"/>
/// marked <c>partial</c> so it can extend that.
/// </summary>
public sealed partial class Order : Aggregate<OrderId, OrderState>
{
    /// <summary>
    /// Place a new order. Transitions Draft → Placed via <see cref="OrderFsm"/>;
    /// raises <see cref="OrderPlaced"/> on success.
    /// </summary>
    public void Place(CustomerId customerId, decimal total, string currency)
    {
        var fsm = new OrderFsm(State.Status);
        if (!fsm.TryFire(OrderTrigger.Place))
        {
            throw new InvalidOperationException($"Cannot place order in status {State.Status}.");
        }

        Raise(new OrderPlaced(Id, customerId, total, currency));
    }

    /// <summary>Setter for newly-minted aggregates that need an id assigned before <c>Raise()</c>.</summary>
    public void SetId(OrderId id) => Id = id;

    protected override OrderState ApplyEvent(OrderState state, object @event) => @event switch
    {
        OrderPlaced p => state.Apply(p),
        _ => state,
    };
}
