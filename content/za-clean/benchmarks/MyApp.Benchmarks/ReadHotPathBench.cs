using System.Data.Async;
using System.Data.Async.Adapters;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.Benchmarks;

/// <summary>
/// Focused read hot-path allocation benchmark for the za-clean ORM layer.
/// Exercises <see cref="OrderRepository.GetByIdAsync"/> directly against an
/// in-memory SQLite connection — no HTTP, no mediator, no serialization.
/// The measured allocation is the "framework hot path" the README claim
/// scopes to (closes #164).
///
/// <para>
/// Setup seeds one Order with two OrderLines so the multi-result-set read
/// path is fully exercised (the [Query] SQL is a head + lines join).
/// </para>
///
/// <para>
/// <b>Reproduce:</b>
/// <code>
/// cd content/za-clean
/// dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"
/// </code>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ReadHotPathBench
{
    private SqliteConnection? _conn;
    private IAsyncDbConnection? _async;
    private OrderRepository? _repo;
    private OrderId _seededId;

    [GlobalSetup]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _async = _conn.AsAsync();
        ApplyMigrations(_async);

        _repo = new OrderRepository(_async);

        // Seed a known order with two lines so GetByIdAsync exercises the
        // head + lines multi-result-set path that the [Query] SQL drives.
        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-A", 1, Money.TryCreate(10m, "EUR").Value);
        order.AddLine("SKU-B", 1, Money.TryCreate(5m, "EUR").Value);
        _repo.AddAsync(order, default).GetAwaiter().GetResult();
        _seededId = order.Id;
    }

    // Hand-written baseline issuing the same ;-joined head+lines SELECT
    // via raw SqliteCommand + ExecuteReaderAsync + NextResultAsync + manual
    // materialize. Does the same MoneyConverter.FromStorage roundtrip for
    // Total + Price so the comparison is apples-to-apples (both paths pay
    // the TEXT-to-Money decode). Mirrors ZA.ORM's MultiResultSetBench.
    [Benchmark(Baseline = true)]
    public async Task<Order?> HandWrittenAdoNet()
    {
        await using var cmd = _conn!.CreateCommand();
        cmd.CommandText =
            "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
            "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = _seededId.Value;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }
        var customerId = reader.GetInt32(0);
        var status = reader.GetString(1);
        var totalStr = reader.GetString(2);

        await reader.NextResultAsync().ConfigureAwait(false);
        var lines = new List<OrderLine>(capacity: 2);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            lines.Add(new OrderLine(
                reader.GetString(0),
                reader.GetInt32(1),
                MoneyConverter.FromStorage(reader.GetString(2))));
        }

        return Order.Materialize(
            _seededId,
            new CustomerId(customerId),
            Enum.Parse<OrderStatus>(status),
            MoneyConverter.FromStorage(totalStr),
            lines);
    }

    [Benchmark]
    public async Task<Order?> ZeroAlloc_ORM()
        => await _repo!.GetByIdAsync(_seededId, default).ConfigureAwait(false);

    [GlobalCleanup]
    public void Cleanup() => _conn?.Dispose();

    private static void ApplyMigrations(IAsyncDbConnection conn)
    {
        var source = new EmbeddedResourceMigrationSource(
            typeof(OrderRepository).Assembly,
            "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");
        var runner = new MigrationRunner(conn, source, new SqliteMigrationDialect());
        runner.RunAsync().GetAwaiter().GetResult();
    }
}
