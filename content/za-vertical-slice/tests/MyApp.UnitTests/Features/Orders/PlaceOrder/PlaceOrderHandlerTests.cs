using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.PlaceOrder;

/// <summary>
/// Unit tests for the PlaceOrder slice. Exercises the handler against a
/// kept-alive in-memory SQLite database to keep the seam between EF Core
/// and the slice realistic. The pipeline-level <c>[Validate]</c> behavior
/// is exercised through the integration test (POST /orders with bad
/// input → 400), so this file focuses on the handler's happy path plus a
/// direct invocation of the source-generated validator for the validation
/// rules the slice declares.
/// </summary>
public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_WithValidInput_PersistsAndReturnsOrderId()
    {
        await using var db = NewInMemoryDb();
        var handler = new PlaceOrderHandler(db);
        var cmd = new PlaceOrderCommand(CustomerId: 42, Total: 99.99m);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.Orders.CountAsync());
        var persisted = await db.Orders.SingleAsync();
        Assert.Equal(new CustomerId(42), persisted.CustomerId);
        Assert.Equal(99.99m, persisted.Total);
    }

    [Fact]
    public void PlaceOrderCommand_WithZeroTotal_ValidatorReportsFailure()
    {
        var validator = new PlaceOrderCommandValidator();
        var result = validator.Validate(new PlaceOrderCommand(CustomerId: 42, Total: 0m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.PropertyName == nameof(PlaceOrderCommand.Total));
    }

    [Fact]
    public void PlaceOrderCommand_WithZeroCustomerId_ValidatorReportsFailure()
    {
        var validator = new PlaceOrderCommandValidator();
        var result = validator.Validate(new PlaceOrderCommand(CustomerId: 0, Total: 10m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.PropertyName == nameof(PlaceOrderCommand.CustomerId));
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
