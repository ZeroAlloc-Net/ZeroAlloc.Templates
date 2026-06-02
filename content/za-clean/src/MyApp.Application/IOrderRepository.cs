using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.Application;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);
}
