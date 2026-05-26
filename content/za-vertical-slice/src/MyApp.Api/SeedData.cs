using Microsoft.EntityFrameworkCore;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;

namespace MyApp.Api;

/// <summary>
/// Tiny dev-only seeder. Adds one sample order on a fresh database so smoke-testing
/// <c>GET /orders/{id}</c> works without first issuing a write. Production never sees this.
/// </summary>
internal static class SeedData
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Orders.AnyAsync(ct).ConfigureAwait(false))
            return;

        var price = Money.TryCreate(19.99m, "EUR").Value;
        var order = Order.Create(new CustomerId(1));
        order.AddLine("SKU-DEMO", 2, price);

        await db.Orders.AddAsync(order, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
