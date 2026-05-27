using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Customers.GetCustomer;

public sealed class GetCustomerHandlerTests
{
    [Fact]
    public async Task GetCustomer_WithKnownId_ReturnsDto()
    {
        await using var db = NewInMemoryDb();
        var seeded = new Customer("Acme Ltd.", "billing@acme.example");
        await db.Customers.AddAsync(seeded);
        await db.SaveChangesAsync();

        var handler = new GetCustomerHandler(db);
        var result = await handler.Handle(new GetCustomerQuery(seeded.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.Id, result.Value.Id);
        Assert.Equal("Acme Ltd.", result.Value.Name);
        Assert.Equal("billing@acme.example", result.Value.Email);
    }

    [Fact]
    public async Task GetCustomer_WithUnknownId_ReturnsNotFoundError()
    {
        await using var db = NewInMemoryDb();
        var handler = new GetCustomerHandler(db);

        var result = await handler.Handle(new GetCustomerQuery(new CustomerId(9999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("customer.not_found", result.Error.Code);
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
