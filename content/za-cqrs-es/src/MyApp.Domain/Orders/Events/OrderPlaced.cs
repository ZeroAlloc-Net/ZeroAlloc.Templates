using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;

namespace MyApp.Domain.Orders.Events;

/// <summary>
/// Domain event raised by <see cref="Order.Place"/>. Implements
/// <see cref="INotification"/> so the ZA.EventSourcing.Mediator bridge republishes
/// committed instances through Mediator — <see cref="MyApp.Application.Projections.OrderListingsProjection"/>
/// picks them up and materializes the denormalized read row.
/// </summary>
public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, decimal Total, string Currency)
    : INotification;
