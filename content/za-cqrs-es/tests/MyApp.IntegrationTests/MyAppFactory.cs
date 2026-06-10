using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Infrastructure.Projections;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.IntegrationTests;

/// <summary>
/// Hosts the API with a kept-alive in-memory SQLite connection for the read-side
/// projection storage. The event store stays as the in-memory adapter (default
/// composition in Program.cs). ZA.ORM migrations are applied once during
/// fixture construction; the startup migration runner in Program.cs is
/// short-circuited via <c>Database:SchemaStrategy=Skip</c>.
/// </summary>
public class MyAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IAsyncDbConnection _asyncConn;

    public MyAppFactory()
    {
        _connection.Open();
        _asyncConn = _connection.AsAsync();

        var source = new EmbeddedResourceMigrationSource(
            typeof(OrderListingsRepository).Assembly,
            "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");
        var dialect = new SqliteMigrationDialect();
        var runner = new MigrationRunner(_asyncConn, source, dialect);
        runner.RunAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration) reaches Program.cs's
        // top-level Configuration reads under WebApplicationFactory<Program>
        // — see #198 for the bug this works around.
        builder.UseSetting("Jwt:DevSigningKey", TestJwt.DevKey);
        builder.UseSetting("Database:SchemaStrategy", "Skip");

        builder.ConfigureServices(services =>
        {
            // Replace the scoped IAsyncDbConnection with the kept-alive in-memory wrapper.
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAsyncDbConnection));
            if (dbDescriptor is not null)
            {
                services.Remove(dbDescriptor);
            }
            services.AddSingleton<IAsyncDbConnection>(_ => _asyncConn);
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
