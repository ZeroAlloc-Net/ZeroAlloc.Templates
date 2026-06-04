using System.Data.Async;
using System.Data.Async.Adapters;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using MyApp.Application.GetOrderById;
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
    // materialize. Projects directly into <see cref="OrderReadModel"/> using
    // the same flattening helpers (<see cref="MoneyConverter.AmountFromStorage"/>
    // for per-line price, <see cref="MoneyConverter.FromStorage"/> for the
    // head total) so the comparison is apples-to-apples against the
    // ZA.ORM-generated read path (post-#173 flat read model).
    [Benchmark(Baseline = true)]
    public async Task<OrderReadModel?> HandWrittenAdoNet()
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
        // Two-line seed; size exactly to avoid List growth allocs in baseline.
        var lines = new List<OrderLineReadModel>(capacity: 2);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            lines.Add(new OrderLineReadModel(
                reader.GetString(0),
                reader.GetInt32(1),
                MoneyConverter.AmountFromStorage(reader.GetString(2))));
        }

        var total = MoneyConverter.FromStorage(totalStr);
        return new OrderReadModel(
            _seededId,
            new CustomerId(customerId),
            status,
            total.Amount,
            total.Currency,
            lines);
    }

    [Benchmark]
    public async Task<OrderReadModel?> ZeroAlloc_ORM()
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
