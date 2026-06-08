using System.Data.Async;
using System.Data.Async.Adapters;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Common;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.ORM.Migrations;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

/// <summary>
/// In-process mediator dispatch benchmark for the GET /orders/{id} read path
/// in the za-vertical-slice template. Mirrors the za-clean
/// <c>MediatorDispatchBench</c> shape so the per-call dispatch cost can be
/// compared directly (issue #189) — same in-memory SQLite, same stub
/// security context, same per-call scope acquisition, same number of seeded
/// rows. The vs schema is single-table (Orders only, no OrderLines), so the
/// seed inserts a single order at €15 rather than the two-line €10+€5 shape
/// the clean template uses; the bench measures dispatch overhead and the
/// vs's <c>GetOrderHandler</c> only ever issues a single-row read.
///
/// <para>
/// Composes a minimal service graph by hand: ZA.Mediator + ZA.Mediator.Validation
/// + ZA.Mediator.Authorization (with the same stub
/// <see cref="ISecurityContextAccessor"/> shape as clean), the ZA.Inject-emitted
/// scoped registration for <see cref="GetOrderHandler"/>, and a singleton
/// in-memory SQLite connection. No HTTP factory, no JWT bearer, no Kestrel.
/// Seeds via a direct <see cref="PlaceOrderHandler"/> construction (sidestepping
/// the mediator pipeline) so seed cost isn't on the measured path.
/// </para>
///
/// <para>
/// <b>Reproduce:</b>
/// <code>
/// cd content/za-vertical-slice/benchmarks/MyApp.Benchmarks
/// dotnet run -c Release -- --filter "*MediatorDispatchBench*"
/// </code>
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MediatorDispatchBench
{
    private ServiceProvider? _root;
    private SqliteConnection? _conn;
    private GetOrderQuery _query;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));

        // Singleton :memory: SQLite — scoped IAsyncDbConnection lifetimes
        // would tear the connection down between dispatches.
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var async = _conn.AsAsync();
        services.AddSingleton<IAsyncDbConnection>(async);

        // Stub security context accessor — the [RequirePolicy("OrdersRead")]
        // authorization behaviour in front of the GetOrder handler will see a
        // context carrying the orders.read + orders.write scopes, so every
        // dispatch passes authorization (matches the WritePipelineBench JWT).
        services.AddScoped<StubSecurityContextAccessor>();
        services.AddMyApp(o => o.UseAccessor<StubSecurityContextAccessor>());

        _root = services.BuildServiceProvider();

        ApplyMigrations(async);

        // Seed via a direct PlaceOrderHandler construction — the seed shouldn't
        // be on the measured path, and constructing the handler by hand is
        // simpler than threading the validator + authorization behaviours just
        // to materialise one row.
        var seedHandler = new PlaceOrderHandler(async);
        var placeResult = seedHandler.Handle(
            new PlaceOrderCommand(new CustomerId(42), 15m),
            default).GetAwaiter().GetResult();
        _query = new GetOrderQuery(placeResult.Value);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _root?.Dispose();
        _conn?.Dispose();
    }

    [Benchmark]
    public async Task<Result<OrderDto, Error>> Dispatch()
    {
        using var scope = _root!.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(_query, default).ConfigureAwait(false);
    }

    private static void ApplyMigrations(IAsyncDbConnection conn)
    {
        var source = new EmbeddedResourceMigrationSource(
            typeof(PlaceOrderHandler).Assembly,
            "MyApp.Persistence.Migrations.Sqlite.");
        var runner = new MigrationRunner(conn, source, new SqliteMigrationDialect());
        runner.RunAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Bench-only <see cref="ISecurityContextAccessor"/> that always returns a
/// context carrying <c>orders.read</c> and <c>orders.write</c> scopes — same
/// shape the JWT bearer middleware would have produced under the
/// WritePipelineBench harness. Keeps the [RequirePolicy] authorization
/// behaviour on the hot path (it still runs and evaluates), but the policy
/// always allows.
/// </summary>
internal sealed class StubSecurityContextAccessor : ISecurityContextAccessor
{
    public ISecurityContext Current { get; } = new StubContext();

    private sealed class StubContext : ISecurityContext
    {
        public string Id => "bench-user";
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string> Claims { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "scope", "orders.read orders.write" },
            };
    }
}
