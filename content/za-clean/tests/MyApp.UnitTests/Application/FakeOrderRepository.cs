using MyApp.Application;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.UnitTests.Application;

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Saved { get; } = new();

    public Task<Order> AddAsync(Order order, CancellationToken ct)
    {
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

    public Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var match = Saved.FirstOrDefault(o => o.Id.Value == id.Value);
        if (match is null) return Task.FromResult<OrderReadModel?>(null);

        var lines = new OrderLineReadModel[match.Lines.Count];
        for (var i = 0; i < match.Lines.Count; i++)
        {
            var l = match.Lines[i];
            lines[i] = new OrderLineReadModel(l.Sku, l.Quantity, l.Price.Amount);
        }
        return Task.FromResult<OrderReadModel?>(new OrderReadModel(
            match.Id,
            match.CustomerId,
            match.Status.ToString(),
            match.Total.Amount,
            match.Total.Currency,
            lines));
    }
}
