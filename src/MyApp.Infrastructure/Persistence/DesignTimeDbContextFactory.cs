using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyApp.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by the EF Core tooling (`dotnet ef migrations add`,
/// `dotnet ef database update`) to construct an <see cref="AppDbContext"/>
/// without running the host's DI container.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design.db")
            .Options;

        return new AppDbContext(options);
    }
}
