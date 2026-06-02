using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.Api;

/// <summary>
/// Tiny dev-only seeder. Adds one sample order on a fresh database so smoke-testing
/// <c>GET /orders/{id}</c> works without first issuing a write. Production never sees this.
/// </summary>
internal static class SeedData
{
    public static async Task SeedAsync(IOrderRepository repo, CancellationToken ct = default)
    {
        if (await repo.CountAsync(ct).ConfigureAwait(false) > 0)
            return;

        var price = Money.TryCreate(19.99m, "EUR").Value;
        var order = Order.Create(new CustomerId(1));
        order.AddLine("SKU-DEMO", 2, price);

        await repo.AddAsync(order, ct).ConfigureAwait(false);
    }
}
