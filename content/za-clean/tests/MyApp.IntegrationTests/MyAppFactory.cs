using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Infrastructure.Persistence;

namespace MyApp.IntegrationTests;

/// <summary>
/// Hosts the API with two swaps shared across integration test classes: the
/// EF DbContext is rebound to a kept-alive in-memory SQLite connection, and
/// <c>IShippingQuoteClient</c> is replaced by a deterministic stub. Migrations
/// from <c>MyApp.Infrastructure</c> are applied against the in-memory database
/// during fixture construction.
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

            var shippingDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IShippingQuoteClient));
            if (shippingDescriptor is not null)
            {
                services.Remove(shippingDescriptor);
            }

            services.AddScoped<IShippingQuoteClient, TestShippingClient>();
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
