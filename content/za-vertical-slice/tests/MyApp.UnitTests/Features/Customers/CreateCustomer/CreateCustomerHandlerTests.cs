using Microsoft.EntityFrameworkCore;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Customers.CreateCustomer;

public sealed class CreateCustomerHandlerTests
{
    [Fact]
    public async Task CreateCustomer_WithValidInput_PersistsAndReturnsCustomerId()
    {
        await using var db = NewInMemoryDb();
        var handler = new CreateCustomerHandler(db);
        var cmd = new CreateCustomerCommand(Name: "Acme Ltd.", Email: "billing@acme.example");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await db.Customers.SingleAsync();
        Assert.Equal("Acme Ltd.", persisted.Name);
        Assert.Equal("billing@acme.example", persisted.Email);
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
