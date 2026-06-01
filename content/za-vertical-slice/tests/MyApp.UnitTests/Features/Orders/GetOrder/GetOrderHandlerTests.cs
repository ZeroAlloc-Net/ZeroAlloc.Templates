using System.Data.Async;
using MyApp.Common;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.PlaceOrder;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.GetOrder;

public sealed class GetOrderHandlerTests
{
    [Fact]
    public async Task GetOrder_WithKnownId_ReturnsDto()
    {
        await using var db = new TestDb();
        var orderId = await InsertOrderAsync(db.Connection, customerId: 7, total: 42.50m);

        var handler = new GetOrderHandler(db.Connection);
        var result = await handler.Handle(new GetOrderQuery(new OrderId(orderId)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new OrderId(orderId), result.Value.Id);
        Assert.Equal(new CustomerId(7), result.Value.CustomerId);
        Assert.Equal(42.50m, result.Value.Total);
    }

    [Fact]
    public async Task GetOrder_WithUnknownId_ReturnsNotFoundError()
    {
        await using var db = new TestDb();
        var handler = new GetOrderHandler(db.Connection);

        var result = await handler.Handle(new GetOrderQuery(new OrderId(9999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("order.not_found", result.Error.Code);
    }

    private static async Task<int> InsertOrderAsync(IAsyncDbConnection conn, int customerId, decimal total)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"";
        AddParam(cmd, "@customerId", customerId);
        AddParam(cmd, "@total", total);
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
