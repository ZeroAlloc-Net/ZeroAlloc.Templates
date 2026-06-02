using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.PlaceOrder;

/// <summary>
/// Unit tests for the PlaceOrder slice. Exercises the handler against a
/// kept-alive in-memory SQLite database (via <see cref="TestDb"/>) so the
/// generator-emitted [Command]/[Query] partials run against a real ADO.NET
/// surface. The pipeline-level <c>[Validate]</c> behavior is exercised
/// through the integration test (POST /orders with bad input → 400), so
/// this file focuses on the handler's happy path plus a direct invocation
/// of the source-generated validator for the validation rules the slice
/// declares.
/// </summary>
public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_WithValidInput_PersistsAndReturnsOrderId()
    {
        await using var db = new TestDb();
        var handler = new PlaceOrderHandler(db.Connection);
        var cmd = new PlaceOrderCommand(CustomerId: new CustomerId(42), Total: 99.99m);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var countCmd = db.Connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM \"Orders\"";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        Assert.Equal(1, count);

        var readCmd = db.Connection.CreateCommand();
        readCmd.CommandText = "SELECT \"CustomerId\", \"Total\" FROM \"Orders\"";
        await using var reader = await readCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetInt32(0));
        Assert.Equal(99.99m, reader.GetDecimal(1));
    }

    [Fact]
    public void PlaceOrderCommand_WithZeroTotal_ValidatorReportsFailure()
    {
        var validator = new PlaceOrderCommandValidator();
        var result = validator.Validate(new PlaceOrderCommand(CustomerId: new CustomerId(42), Total: 0m));
        Assert.False(result.IsValid);
        Assert.True(HasFailureOn(result.Failures, nameof(PlaceOrderCommand.Total)));
    }

    // The validator's Failures is a ReadOnlySpan<ValidationFailure> (ref struct),
    // which can't be passed as a generic type argument to Assert.Contains. Iterate
    // manually instead.
    private static bool HasFailureOn(ReadOnlySpan<ZeroAlloc.Validation.ValidationFailure> failures, string propertyName)
    {
        foreach (var f in failures)
        {
            if (f.PropertyName == propertyName)
            {
                return true;
            }
        }

        return false;
    }
}
