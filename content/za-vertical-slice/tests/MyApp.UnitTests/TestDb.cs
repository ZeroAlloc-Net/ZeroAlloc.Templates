using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.UnitTests;

/// <summary>
/// Kept-alive in-memory Sqlite database with ZA.ORM migrations applied once
/// per fixture. Hands out a shared <see cref="IAsyncDbConnection"/> wrapper
/// for handler ctor injection — generator-emitted partials check
/// connection state and skip Open/Close when the connection is already open,
/// so the wrapper is safe to share across handler invocations in a test.
/// </summary>
internal sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _raw = new("DataSource=:memory:");

    public TestDb()
    {
        _raw.Open();
        Connection = _raw.AsAsync();

        var source = new EmbeddedResourceMigrationSource(
            typeof(PlaceOrderHandler).Assembly,
            "MyApp.Persistence.Migrations.Sqlite.");
        var runner = new MigrationRunner(Connection, source, new SqliteMigrationDialect());
        runner.RunAsync().GetAwaiter().GetResult();
    }

    public IAsyncDbConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync().ConfigureAwait(false);
        _raw.Dispose();
    }
}
