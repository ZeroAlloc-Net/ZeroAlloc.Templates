using Microsoft.EntityFrameworkCore;
using MyApp.Domain;
using MyApp.Infrastructure.Persistence.CompiledModel;

namespace MyApp.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Compiled model required for AOT publish; regenerate via
            // `dotnet ef dbcontext optimize` after any entity/mapping change.
            optionsBuilder.UseModel(AppDbContextModel.Instance);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
