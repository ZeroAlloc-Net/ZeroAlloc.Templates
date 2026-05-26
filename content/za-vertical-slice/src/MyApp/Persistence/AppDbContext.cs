using Microsoft.EntityFrameworkCore;
using MyApp.Common;

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
// Stub entity placeholders.
//
// These exist so AppDbContext + the initial migration scaffold compile before
// the slices land. In Phase 3 each owning slice REPLACES its matching stub
// inline within the slice file:
//   - PlaceOrder.cs takes over the real Order definition
//   - CreateCustomer.cs takes over the real Customer definition
// At that point the slice commit deletes the corresponding stub from THIS file.
//
// Both stubs use a private parameterless constructor so EF Core can materialize
// instances via reflection while preventing arbitrary external construction.
// ---------------------------------------------------------------------------

public sealed class Order
{
    public OrderId Id { get; private set; }

    private Order()
    {
    }
}

public sealed class Customer
{
    public CustomerId Id { get; private set; }

    private Customer()
    {
    }
}
