using Microsoft.EntityFrameworkCore;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;

namespace MyApp.Infrastructure.Persistence;

[Scoped]
public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        await db.Orders.AddAsync(order, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
        => db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
}
