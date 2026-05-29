using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyApp.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by the EF Core tooling (`dotnet ef migrations add`,
/// `dotnet ef migrations script`) to construct an <see cref="AppDbContext"/>
/// without running the host's DI container.
///
/// Accepts a `--provider Sqlite|Postgres` argument (passed after `--` on the
/// `dotnet ef` command line) so the same factory scaffolds both migration
/// histories. Falls back to the `DOTNET_EF_PROVIDER` env var, then to Sqlite.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Handle both "--provider Postgres" (space-separated) and
        // "--provider=Postgres" (equals-separated) forms.
        var provider = args.FirstOrDefault(a => a.StartsWith("--provider=", StringComparison.Ordinal))
            ?.Split('=', 2).Skip(1).FirstOrDefault()
            ?? args.SkipWhile(a => !string.Equals(a, "--provider", StringComparison.Ordinal)).Skip(1).FirstOrDefault()
            ?? Environment.GetEnvironmentVariable("DOTNET_EF_PROVIDER")
            ?? "Sqlite";

        // Validate explicitly: a typo (`--provider Postgrse`) would otherwise silently
        // fall through to Sqlite and overwrite the wrong migration folder. Fail loudly
        // at design time instead.
        if (!string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"DesignTimeDbContextFactory: unknown provider '{provider}'. Expected 'Sqlite' or 'Postgres'.",
                nameof(args));
        }

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql(
                "Host=localhost;Database=design;Username=postgres;Password=postgres",
                npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
        }
        else
        {
            builder.UseSqlite(
                "Data Source=design.db",
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
        }

        return new AppDbContext(builder.Options);
    }
}
