using System.Data.Async;
using MyApp.Common;
using MyApp.Features.Customers.GetCustomer;
using Xunit;

namespace MyApp.UnitTests.Features.Customers.GetCustomer;

public sealed class GetCustomerHandlerTests
{
    [Fact]
    public async Task GetCustomer_WithKnownId_ReturnsDto()
    {
        await using var db = new TestDb();
        var customerId = await InsertCustomerAsync(db.Connection, "Acme Ltd.", "billing@acme.example");

        var handler = new GetCustomerHandler(db.Connection);
        var result = await handler.Handle(new GetCustomerQuery(new CustomerId(customerId)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CustomerId(customerId), result.Value.Id);
        Assert.Equal("Acme Ltd.", result.Value.Name);
        Assert.Equal("billing@acme.example", result.Value.Email);
    }

    [Fact]
    public async Task GetCustomer_WithUnknownId_ReturnsNotFoundError()
    {
        await using var db = new TestDb();
        var handler = new GetCustomerHandler(db.Connection);

        var result = await handler.Handle(new GetCustomerQuery(new CustomerId(9999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("customer.not_found", result.Error.Code);
    }

    private static async Task<int> InsertCustomerAsync(IAsyncDbConnection conn, string name, string email)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO \"Customers\" (\"Name\", \"Email\") VALUES (@name, @email) RETURNING \"Id\"";
        AddParam(cmd, "@name", name);
        AddParam(cmd, "@email", email);
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
