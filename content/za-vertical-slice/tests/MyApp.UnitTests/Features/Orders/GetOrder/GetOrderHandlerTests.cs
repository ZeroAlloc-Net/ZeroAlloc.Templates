using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.GetOrder;

public sealed class GetOrderHandlerTests
{
    [Fact]
    public async Task GetOrder_WithKnownId_ReturnsDto()
    {
        await using var db = NewInMemoryDb();
        var seeded = new Order(new CustomerId(7), 42.50m);
        await db.Orders.AddAsync(seeded);
        await db.SaveChangesAsync();

        var handler = new GetOrderHandler(db);
        var result = await handler.Handle(new GetOrderQuery(seeded.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.Id.Value, result.Value.Id);
        Assert.Equal(7, result.Value.CustomerId);
        Assert.Equal(42.50m, result.Value.Total);
    }

    [Fact]
    public async Task GetOrder_WithUnknownId_ReturnsNotFoundError()
    {
        await using var db = NewInMemoryDb();
        var handler = new GetOrderHandler(db);

        var result = await handler.Handle(new GetOrderQuery(9999), CancellationToken.None);

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
