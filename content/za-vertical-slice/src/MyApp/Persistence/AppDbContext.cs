using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;

namespace MyApp.Persistence;

/// <summary>
/// EF Core DbContext for the vertical-slice template. Each slice owns its entity
/// definition inline (PlaceOrder owns <see cref="Order"/>, CreateCustomer owns
/// <see cref="Customer"/>); the DbSets + typed-id value-object conversions are
/// declared centrally here so the model + migrations have a single source of truth.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id)
                .HasConversion(id => id.Value, value => new OrderId(value));
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id)
                .HasConversion(id => id.Value, value => new CustomerId(value));
        });

        base.OnModelCreating(modelBuilder);
    }
}

// ---------------------------------------------------------------------------
// Stub entity placeholder.
//
// The real Order entity is owned by the PlaceOrder slice
// (Features/Orders/PlaceOrder/PlaceOrder.cs). Customer is still a stub until
// the CreateCustomer slice lands — it will replace this declaration with the
// real definition and delete the stub in the same commit.
//
// The stub uses a private parameterless constructor so EF Core can materialize
// instances via reflection while preventing arbitrary external construction.
// ---------------------------------------------------------------------------

public sealed class Customer
{
    public CustomerId Id { get; private set; }

    private Customer()
    {
    }
}
