using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Persistence;

namespace MyApp.IntegrationTests;

/// <summary>
/// Hosts the API with the EF DbContext rebound to a kept-alive in-memory SQLite
/// connection so every integration test runs against a clean, isolated schema
/// without needing a file on disk. Slices that need additional service
/// overrides extend this fixture via <c>WithWebHostBuilder(...)</c>.
/// </summary>
public sealed class MyAppFactory : WebApplicationFactory<Program>
{
    // Connection opened once per fixture and kept alive for the test session;
    // SQLite's :memory: database is bound to the connection's lifetime.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MyAppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
            {
                services.Remove(dbDescriptor);
            }

            services.AddDbContext<AppDbContext>(opt =>
            {
                opt.UseSqlite(_connection, sqlite =>
                    sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                opt.ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            });

            // Create the schema for each test fixture so slice tests can hit the
            // database without a migration step.
            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
