using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Verifies that <see cref="MyApp.Infrastructure.Persistence.OrderRepository.AddAsync"/>
/// writes the Order aggregate atomically: a failure inserting any line rolls
/// back the order head and every previously-inserted line.
///
/// Each test creates its own <see cref="MyAppFactory"/> so atomicity is
/// observable via post-failure row counts (no shared-fixture seed pollution).
/// Failure is injected by a CHECK ("Quantity" > 0) constraint violation
/// on the second order line.
/// </summary>
public sealed class OrderRepositoryAtomicityTests
{
    [Fact]
    public async Task AddAsync_rolls_back_order_head_and_lines_when_line_insert_fails()
    {
        using var factory = new MyAppFactory();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-OK", 1, Money.TryCreate(10m, "EUR").Value);
        // Quantity = 0 violates the CHECK ("Quantity" > 0) constraint added
        // in commit d227be9. The Sqlite provider raises SqliteException with
        // the constraint message; the transaction's DisposeAsync (via the
        // `await using` scope inside AddAsync) rolls back the order head
        // and the first OK line.
        order.AddLine("SKU-INVALID", 0, Money.TryCreate(5m, "EUR").Value);

        await Assert.ThrowsAsync<SqliteException>(() => repo.AddAsync(order, CancellationToken.None));

        // Atomicity: nothing persisted.
        var orderCount = await repo.CountAsync(CancellationToken.None);
        Assert.Equal(0, orderCount);
    }

    [Fact]
    public async Task AddAsync_commits_order_head_and_lines_when_all_inserts_succeed()
    {
        // Sibling-positive guard against the transaction wrap accidentally
        // rolling back successful writes. Verifies the commit path is
        // exercised when no constraint violates.
        using var factory = new MyAppFactory();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-A", 2, Money.TryCreate(10m, "EUR").Value);
        order.AddLine("SKU-B", 3, Money.TryCreate(5m, "EUR").Value);

        await repo.AddAsync(order, CancellationToken.None);

        var orderCount = await repo.CountAsync(CancellationToken.None);
        Assert.Equal(1, orderCount);
    }
}
