using System.Threading;
using System.Threading.Tasks;
using MyApp.Domain.Orders;
using MyApp.Domain.Orders.Events;
using ZeroAlloc.Mediator;

namespace MyApp.Application.Projections;

/// <summary>
/// Materializes the <c>order_listings</c> denormalized read row when an
/// <see cref="OrderPlaced"/> event is republished through the
/// <c>EventStoreMediatorBridge</c>. Idempotent — relies on
/// <see cref="IOrderListingsRepository.UpsertAsync"/> so re-delivery of the
/// same event is safe.
/// </summary>
public sealed class OrderListingsProjection(IOrderListingsRepository repo)
    : INotificationHandler<OrderPlaced>
{
    public async ValueTask Handle(OrderPlaced notification, CancellationToken ct)
    {
        await repo.UpsertAsync(
            notification.OrderId,
            notification.CustomerId,
            OrderStatus.Placed.ToString(),
            notification.Total,
            notification.Currency,
            ct).ConfigureAwait(false);
    }
}
