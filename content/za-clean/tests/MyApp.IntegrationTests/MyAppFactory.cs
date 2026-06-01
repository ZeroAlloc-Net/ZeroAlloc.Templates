using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Infrastructure.Persistence;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.IntegrationTests;

/// <summary>
/// Hosts the API with two swaps shared across integration test classes: the
/// scoped <see cref="IAsyncDbConnection"/> registration is rebound to a
/// kept-alive in-memory SQLite connection, and <c>IShippingQuoteClient</c> is
/// replaced by a deterministic stub. ZA.ORM migrations from
/// <c>MyApp.Infrastructure</c> are applied once against the in-memory database
/// during fixture construction. The startup MigrationRunner block in Program.cs
/// is short-circuited via <c>Database:SchemaStrategy=Skip</c> so it does not
/// try to apply migrations against a transient connection.
/// </summary>
public sealed class MyAppFactory : WebApplicationFactory<Program>
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
            typeof(OrderRepository).Assembly,
            "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");
        var dialect = new SqliteMigrationDialect();
        var runner = new MigrationRunner(_asyncConn, source, dialect);
        runner.RunAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
                // Migrations were applied in the fixture ctor against the
                // kept-alive connection. Tell Program.cs's startup block to
                // skip its own migration apply (which would otherwise build a
                // transient connection against the configured string and miss
                // the in-memory database entirely).
                ["Database:SchemaStrategy"] = "Skip",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the scoped IAsyncDbConnection registration with one that
            // always returns the kept-alive in-memory connection wrapper. The
            // ZA.ORM generator's ref-counted lifecycle checks conn.State before
            // opening — since _connection is already open, the generated code
            // neither opens nor closes it, so sharing a single wrapper across
            // request scopes is safe.
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IAsyncDbConnection));
            if (dbDescriptor is not null)
            {
                services.Remove(dbDescriptor);
            }
            services.AddScoped<IAsyncDbConnection>(_ => _asyncConn);

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
