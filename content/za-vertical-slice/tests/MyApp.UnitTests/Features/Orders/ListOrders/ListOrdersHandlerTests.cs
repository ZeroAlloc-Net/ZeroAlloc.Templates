using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.ListOrders;

public sealed class ListOrdersHandlerTests
{
    [Fact]
    public async Task ListOrders_ReturnsPageOfItemsWithTotalCount()
    {
        await using var db = NewInMemoryDb();
        for (var i = 1; i <= 5; i++)
        {
            await db.Orders.AddAsync(new Order(new CustomerId(i), i * 10m));
        }
        await db.SaveChangesAsync();

        var handler = new ListOrdersHandler(db);
        var result = await handler.Handle(new ListOrdersQuery(Page: 1, PageSize: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Total);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(3, result.Value.PageSize);
    }

    [Fact]
    public async Task ListOrders_SecondPage_ReturnsRemainder()
    {
        await using var db = NewInMemoryDb();
        for (var i = 1; i <= 5; i++)
        {
            await db.Orders.AddAsync(new Order(new CustomerId(i), i * 10m));
        }
        await db.SaveChangesAsync();

        var handler = new ListOrdersHandler(db);
        var result = await handler.Handle(new ListOrdersQuery(Page: 2, PageSize: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public void ListOrdersQuery_WithPageSizeOver100_ValidatorReportsFailure()
    {
        var validator = new ListOrdersQueryValidator();
        var result = validator.Validate(new ListOrdersQuery(Page: 1, PageSize: 1000));
        Assert.False(result.IsValid);
        var found = false;
        foreach (var f in result.Failures)
        {
            if (f.PropertyName == nameof(ListOrdersQuery.PageSize))
            {
                found = true;
                break;
            }
        }
        Assert.True(found);
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
