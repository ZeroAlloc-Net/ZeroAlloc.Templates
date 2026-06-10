using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.IntegrationTests;

/// <summary>
/// Hosts the API with the scoped <see cref="IAsyncDbConnection"/> registration
/// rebound to a kept-alive in-memory SQLite connection so every integration
/// test runs against a clean, isolated schema without needing a file on disk.
/// ZA.ORM migrations from <c>MyApp.Persistence.Migrations.Sqlite</c> are applied
/// once against the in-memory database during fixture construction. The startup
/// MigrationRunner block in Program.cs is short-circuited via
/// <c>Database:SchemaStrategy=Skip</c> so it does not try to apply migrations
/// against a transient connection. Slices that need additional service overrides
/// extend this fixture via <c>WithWebHostBuilder(...)</c>.
/// </summary>
public class MyAppFactory : WebApplicationFactory<Program>
{
    // Connection opened once per fixture and kept alive for the test session;
    // SQLite's :memory: database is bound to the connection's lifetime.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IAsyncDbConnection _asyncConn;

    public MyAppFactory()
    {
        _connection.Open();
        _asyncConn = _connection.AsAsync();

        // Apply ZA.ORM migrations once against the kept-alive in-memory DB.
        // The connection is already open; MigrationRunner's ref-counted
        // lifecycle respects the "don't close what we didn't open" contract.
        var source = new EmbeddedResourceMigrationSource(
            typeof(PlaceOrderHandler).Assembly,
            "MyApp.Persistence.Migrations.Sqlite.");
        var dialect = new SqliteMigrationDialect();
        var runner = new MigrationRunner(_asyncConn, source, dialect);
        runner.RunAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Use UseSetting (not ConfigureAppConfiguration) so the overrides reach
        // Program.cs's top-level Configuration reads under minimal-host
        // WebApplicationFactory<Program>. ConfigureAppConfiguration callbacks fire
        // after the WebApplicationBuilder has already finished reading top-level
        // config — too late for the migration-skip override to take effect, which
        // silently caused Program.cs to run a duplicate (no-op) migration against
        // a transient connection on every test factory creation. UseSetting writes
        // to the host configuration that the minimal-host bridge does observe.
        builder.UseSetting("Jwt:DevSigningKey", TestJwt.DevKey);
        builder.UseSetting("Database:SchemaStrategy", "Skip");

        builder.ConfigureServices(services =>
        {
            // Replace the scoped IAsyncDbConnection registration with a
            // singleton that always returns the kept-alive in-memory connection
            // wrapper. The ZA.ORM generator's ref-counted lifecycle checks
            // conn.State before opening — since _connection is already open,
            // the generated code neither opens nor closes it, so sharing a
            // single wrapper across request scopes is safe. Singleton lifetime
            // is important: a scoped registration would cause DI to dispose
            // the wrapper at the end of each request scope, which closes the
            // underlying SqliteConnection and drops the :memory: schema, so
            // the second test in the class would see "no such table" errors.
            // Disposal of the underlying connection is owned by this fixture
            // (see Dispose(bool) below).
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IAsyncDbConnection));
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
