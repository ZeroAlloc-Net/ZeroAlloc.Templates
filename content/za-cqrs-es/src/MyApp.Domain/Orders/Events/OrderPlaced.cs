using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;

namespace MyApp.Domain.Orders.Events;

/// <summary>
/// Domain event raised by <see cref="Order.Place"/>. Implements
/// <see cref="INotification"/> so <see cref="MyApp.Application.Orders.PlaceOrder.PlaceOrderHandler"/>
/// can hand a committed instance to <c>IMediator.Publish</c> — every
/// registered <see cref="INotificationHandler{TNotification}"/> (notably
/// <see cref="MyApp.Application.Projections.OrderListingsProjection"/>)
/// runs synchronously inside the request scope, keeping read rows in
/// lock-step with the write.
/// </summary>
public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, decimal Total, string Currency)
    : INotification;
