using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence.CompiledModel;

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Compiled model required for AOT publish — EF Core 10 refuses to
            // build the model at runtime under PublishAot=true. Regenerate via
            // `dotnet ef dbcontext optimize -o Persistence/CompiledModel` after
            // any entity/mapping change. Integration tests / WebApplicationFactory
            // pre-configure options with their own UseSqlite call, so this
            // branch only fires for the production Program.cs DI path.
            optionsBuilder.UseModel(AppDbContextModel.Instance);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id)
                .HasConversion(id => id.Value, value => new OrderId(value))
                .ValueGeneratedOnAdd();
            b.Property(o => o.CustomerId)
                .HasConversion(id => id.Value, value => new CustomerId(value));
            b.Property(o => o.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id)
                .HasConversion(id => id.Value, value => new CustomerId(value))
                .ValueGeneratedOnAdd();
            b.Property(c => c.Name).IsRequired();
            b.Property(c => c.Email).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

