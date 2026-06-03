using System.Data.Async;
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
        await using var db = new TestDb();
        var orderId = await InsertOrderAsync(db.Connection, customerId: 1, total: 99m, status: nameof(OrderStatus.Pending));

        var handler = new CancelOrderHandler(db.Connection);
        var result = await handler.Handle(new CancelOrderCommand(new OrderId(orderId)), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var readCmd = db.Connection.CreateCommand();
        readCmd.CommandText = "SELECT \"Status\" FROM \"Orders\" WHERE \"Id\" = @id";
        var idParam = readCmd.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = orderId;
        readCmd.Parameters.Add(idParam);
        var status = (string?)await readCmd.ExecuteScalarAsync();
        Assert.Equal(nameof(OrderStatus.Cancelled), status);
    }

    [Fact]
    public async Task CancelOrder_WhenAlreadyCancelled_ReturnsConflict()
    {
        await using var db = new TestDb();
        var orderId = await InsertOrderAsync(db.Connection, customerId: 1, total: 99m, status: nameof(OrderStatus.Cancelled));

        var handler = new CancelOrderHandler(db.Connection);
        var result = await handler.Handle(new CancelOrderCommand(new OrderId(orderId)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal("order.already_cancelled", result.Error.Code);
    }

    [Fact]
    public async Task CancelOrder_WhenUnknownId_ReturnsNotFound()
    {
        await using var db = new TestDb();
        var handler = new CancelOrderHandler(db.Connection);

        var result = await handler.Handle(new CancelOrderCommand(new OrderId(9999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("order.not_found", result.Error.Code);
    }

    private static async Task<int> InsertOrderAsync(IAsyncDbConnection conn, int customerId, decimal total, string status)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"";
        AddParam(cmd, "@customerId", customerId);
        // Total is stored as Money's wire shape "<amount>|<currency>" — encode the
        // decimal via MoneyConverter so the column matches the production schema.
        AddParam(cmd, "@total", MoneyConverter.ToStorage(Money.TryCreate(total, "EUR").Value));
        AddParam(cmd, "@status", status);
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
