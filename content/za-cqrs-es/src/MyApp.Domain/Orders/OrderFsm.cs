using ZeroAlloc.StateMachine;

#pragma warning disable MA0048 // OrderTrigger enum co-located with OrderFsm by design

namespace MyApp.Domain.Orders;

/// <summary>Aggregate-level lifecycle triggers fired by <see cref="Order"/> command methods.</summary>
public enum OrderTrigger
{
    Place,
    Ship,
    Cancel,
}

#pragma warning disable ZSM0003 // Triggers each appear in one transition by design — the lifecycle is linear and these single-edge triggers are intentional.
[StateMachine(InitialState = nameof(OrderStatus.Draft))]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Draft,  On = OrderTrigger.Place,  To = OrderStatus.Placed)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Placed, On = OrderTrigger.Ship,   To = OrderStatus.Shipped)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Draft,  On = OrderTrigger.Cancel, To = OrderStatus.Cancelled)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Placed, On = OrderTrigger.Cancel, To = OrderStatus.Cancelled)]
[Terminal<OrderStatus>(State = OrderStatus.Shipped)]
[Terminal<OrderStatus>(State = OrderStatus.Cancelled)]
public sealed partial class OrderFsm
{
    public OrderFsm(OrderStatus current) => _state = current;
}
#pragma warning restore ZSM0003
