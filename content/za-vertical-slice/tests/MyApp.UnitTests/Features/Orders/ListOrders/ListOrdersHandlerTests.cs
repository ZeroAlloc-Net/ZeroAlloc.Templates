using System.Data.Async;
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
        await using var db = new TestDb();
        for (var i = 1; i <= 5; i++)
        {
            await InsertOrderAsync(db.Connection, customerId: i, total: i * 10m);
        }

        var handler = new ListOrdersHandler(db.Connection);
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
        await using var db = new TestDb();
        for (var i = 1; i <= 5; i++)
        {
            await InsertOrderAsync(db.Connection, customerId: i, total: i * 10m);
        }

        var handler = new ListOrdersHandler(db.Connection);
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

    private static async Task<int> InsertOrderAsync(IAsyncDbConnection conn, int customerId, decimal total)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"";
        AddParam(cmd, "@customerId", customerId);
        // Total is stored as Money's wire shape "<amount>|<currency>" — encode the
        // decimal via MoneyConverter so the read-side handler can round-trip it.
        AddParam(cmd, "@total", MoneyConverter.ToStorage(Money.TryCreate(total, "EUR").Value));
        AddParam(cmd, "@status", nameof(OrderStatus.Pending));
        var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(id);
    }

    private static void AddParam(IAsyncDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
