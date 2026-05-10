using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.UnitTests.Application;

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Saved { get; } = new();

    public Task AddAsync(Order order, CancellationToken ct)
    {
        Saved.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var match = Saved.FirstOrDefault(o => o.Id.Value == id.Value);
        return Task.FromResult<Order?>(match);
    }
}
