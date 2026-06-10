using System.Threading;
using System.Threading.Tasks;
using MyApp.Domain.Orders;
using MyApp.Domain.Orders.Events;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.EventSourcing.Aggregates;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.Orders.PlaceOrder;

/// <summary>
/// Materializes a fresh <see cref="Order"/> aggregate, raises <c>OrderPlaced</c>,
/// and saves the uncommitted event to the event store via
/// <see cref="IAggregateRepository{TAggregate,TId}"/>. After a successful commit
/// the handler publishes the OrderPlaced notification through
/// <see cref="IMediator"/>, which fans out to every registered
/// <see cref="INotificationHandler{TNotification}"/> — including the
/// <c>OrderListingsProjection</c> that materializes the denormalized read row.
/// </summary>
/// <remarks>
/// Direct in-process publishing (handler → mediator) is used here rather than
/// the per-stream EventStoreMediatorBridge: the bridge subscribes to a single
/// fixed StreamId, which does not match the per-aggregate stream topology this
/// template uses (<c>order-{guid}</c>). A subsequent task may layer in an
/// Outbox-backed background dispatcher once that infrastructure ships — the
/// projection contract on the <see cref="INotificationHandler{TNotification}"/>
/// side is unchanged either way.
/// </remarks>
public sealed class PlaceOrderHandler(
    IAggregateRepository<Order, OrderId> repo,
    IMediator mediator)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, ApplicationError>>
{
    public async ValueTask<Result<OrderId, ApplicationError>> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        var validation = PlaceOrderValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result<OrderId, ApplicationError>.Failure(
                new ApplicationError("validation.failed", $"{validation.Error.Field}: {validation.Error.Message}"));
        }

        var orderId = OrderId.New();
        using var order = new Order();
        order.SetId(orderId);
        try
        {
            order.Place(request.CustomerId, request.Total, request.Currency);
        }
        catch (System.InvalidOperationException ex)
        {
            return Result<OrderId, ApplicationError>.Failure(new ApplicationError("order.place.invalid_state", ex.Message));
        }

        var placed = new OrderPlaced(orderId, request.CustomerId, request.Total, request.Currency);

        var save = await repo.SaveAsync(order, orderId, ct).ConfigureAwait(false);
        if (save.IsFailure)
        {
            return Result<OrderId, ApplicationError>.Failure(
                new ApplicationError("order.persist.failed", save.Error.ToString() ?? "Event store append failed"));
        }

        // Fan out the committed event to every INotificationHandler<OrderPlaced>
        // registration (projections, integration-event publishers, …). Synchronous
        // dispatch keeps the read row in lock-step with the write — Task 5 swaps
        // this for an Outbox-backed background dispatcher.
        await mediator.Publish(placed, ct).ConfigureAwait(false);

        return Result<OrderId, ApplicationError>.Success(orderId);
    }
}
