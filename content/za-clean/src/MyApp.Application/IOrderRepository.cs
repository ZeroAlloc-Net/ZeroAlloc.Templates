using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.Application;

public interface IOrderRepository
{
    /// <summary>
    /// Persists the order aggregate and returns a new <see cref="Order"/>
    /// instance carrying the DB-assigned id. The input <paramref name="order"/>
    /// is unchanged — its <c>Id</c> remains at the sentinel <c>OrderId(0)</c>
    /// from <see cref="Order.Create"/>. Callers must use the returned instance.
    /// </summary>
    Task<Order> AddAsync(Order order, CancellationToken ct);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);
}
