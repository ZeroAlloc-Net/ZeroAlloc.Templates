using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.CancelOrder;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.CancelOrder;

public sealed class CancelOrderHandlerTests
{
    [Fact]
    public async Task CancelOrder_WhenPending_SavesCancelledState()
    {
        await using var db = NewInMemoryDb();
        var order = new Order(new CustomerId(1), 99m);
        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();

        var handler = new CancelOrderHandler(db);
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, persisted.Status);
    }

    [Fact]
    public async Task CancelOrder_WhenAlreadyCancelled_ReturnsConflict()
    {
        await using var db = NewInMemoryDb();
        var order = new Order(new CustomerId(1), 99m);
        order.Cancel();
        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();

        var handler = new CancelOrderHandler(db);
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal("order.already_cancelled", result.Error.Code);
    }

    [Fact]
    public async Task CancelOrder_WhenUnknownId_ReturnsNotFound()
    {
        await using var db = NewInMemoryDb();
        var handler = new CancelOrderHandler(db);

        var result = await handler.Handle(new CancelOrderCommand(new OrderId(9999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("order.not_found", result.Error.Code);
    }

    private static AppDbContext NewInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(opts);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
