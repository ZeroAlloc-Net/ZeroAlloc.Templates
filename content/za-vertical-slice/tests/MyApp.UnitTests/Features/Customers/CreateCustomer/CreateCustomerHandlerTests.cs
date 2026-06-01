using MyApp.Features.Customers.CreateCustomer;
using Xunit;

namespace MyApp.UnitTests.Features.Customers.CreateCustomer;

public sealed class CreateCustomerHandlerTests
{
    [Fact]
    public async Task CreateCustomer_WithValidInput_PersistsAndReturnsCustomerId()
    {
        await using var db = new TestDb();
        var handler = new CreateCustomerHandler(db.Connection);
        var cmd = new CreateCustomerCommand(Name: "Acme Ltd.", Email: "billing@acme.example");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var readCmd = db.Connection.CreateCommand();
        readCmd.CommandText = "SELECT \"Name\", \"Email\" FROM \"Customers\"";
        await using var reader = await readCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Acme Ltd.", reader.GetString(0));
        Assert.Equal("billing@acme.example", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public void CreateCustomerCommand_WithBlankName_ValidatorReportsFailure()
    {
        var validator = new CreateCustomerCommandValidator();
        var result = validator.Validate(new CreateCustomerCommand(Name: "", Email: "ok@example.com"));
        Assert.False(result.IsValid);
        Assert.True(HasFailureOn(result.Failures, nameof(CreateCustomerCommand.Name)));
    }

    [Fact]
    public void CreateCustomerCommand_WithInvalidEmail_ValidatorReportsFailure()
    {
        var validator = new CreateCustomerCommandValidator();
        var result = validator.Validate(new CreateCustomerCommand(Name: "Acme", Email: "not-an-email"));
        Assert.False(result.IsValid);
        Assert.True(HasFailureOn(result.Failures, nameof(CreateCustomerCommand.Email)));
    }

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
