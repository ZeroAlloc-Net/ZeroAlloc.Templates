using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.UnitTests.Application;

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Saved { get; } = new();

    public Task<Order> AddAsync(Order order, CancellationToken ct)
    {
        // Mint a synthetic id (real repo uses INSERT...RETURNING) and
        // materialize a fresh Order so the fake's Saved list + GetByIdAsync
        // lookup mirror the real-repo contract: input order's Id stays at
        // the sentinel; the returned instance carries the assigned id.
        var assigned = Order.Materialize(
            new OrderId(Saved.Count + 1),
            order.CustomerId,
            order.Status,
            order.Total,
            order.Lines);
        Saved.Add(assigned);
        return Task.FromResult(assigned);
    }

    public Task<int> CountAsync(CancellationToken ct)
        => Task.FromResult(Saved.Count);

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var match = Saved.FirstOrDefault(o => o.Id.Value == id.Value);
        return Task.FromResult<Order?>(match);
    }
}
