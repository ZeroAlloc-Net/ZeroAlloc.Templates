using System.Data.Async;
using System.Data.Async.Adapters;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Application;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.ORM.Migrations;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

/// <summary>
/// In-process mediator dispatch benchmark for the GET /orders/{id} read path
/// in the za-clean template. Isolates the mediator pipeline (authorization
/// behaviour + handler resolution + handler execution + repo read) from
/// Kestrel / JWT / JSON / middleware. Used by issue #189 to compare the
/// read-side per-call cost against the vertical-slice template's
/// <c>GetOrderQuery</c> dispatch — if the in-process dispatch cost is roughly
/// equivalent across templates, the 8.6× per-request gap observed in NBomber
/// must live above the mediator in the HTTP layer rather than in the
/// dispatch pipeline itself.
///
/// <para>
/// Composes a minimal service graph by hand: ZA.Mediator + ZA.Mediator.Authorization
/// (with a stub <see cref="ISecurityContextAccessor"/> that always grants
/// orders.read + orders.write), the ZA.Inject-emitted scoped registration for
/// <see cref="OrderRepository"/>, and a singleton in-memory SQLite connection
/// holding the seed data. No HTTP factory, no JWT bearer, no Kestrel.
/// </para>
///
/// <para>
/// <b>Reproduce:</b>
/// <code>
/// cd content/za-clean/benchmarks/MyApp.Benchmarks
/// dotnet run -c Release -- --filter "*MediatorDispatchBench*"
/// </code>
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MediatorDispatchBench
{
    private ServiceProvider? _root;
    private SqliteConnection? _conn;
    private GetOrderByIdQuery _query = null!;

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

        // Repository registration — sidestep AddMyAppInfrastructure to keep
        // the bench's DI graph free of the ZA.Rest typed shipping client and
        // its resilience proxy, neither of which is exercised by the read path.
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Stub security context accessor — the [RequirePolicy("OrdersRead")]
        // authorization behaviour in front of the GetOrderById handler will
        // see a context carrying the orders.read + orders.write scopes, so
        // every dispatch passes authorization (matches the WritePipelineBench
        // JWT contents).
        services.AddScoped<StubSecurityContextAccessor>();
        services.AddMyAppApplication(o => o.UseAccessor<StubSecurityContextAccessor>());

        _root = services.BuildServiceProvider();

        ApplyMigrations(async);

        // Seed via a one-shot scope so the singleton SQLite connection holds
        // the schema + the seeded order for subsequent dispatches. Matches
        // the ReadHotPathBench seed (1 order, 2 lines, customer 42, €10 + €5).
        using var seedScope = _root.CreateScope();
        var repo = seedScope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-A", 1, Money.TryCreate(10m, "EUR").Value);
        order.AddLine("SKU-B", 1, Money.TryCreate(5m, "EUR").Value);
        var saved = repo.AddAsync(order, default).GetAwaiter().GetResult();
        _query = new GetOrderByIdQuery(saved.Id);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _root?.Dispose();
        _conn?.Dispose();
    }

    [Benchmark]
    public async Task<Result<OrderReadModel, ApplicationError>> Dispatch()
    {
        using var scope = _root!.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(_query, default).ConfigureAwait(false);
    }

    private static void ApplyMigrations(IAsyncDbConnection conn)
    {
        var source = new EmbeddedResourceMigrationSource(
            typeof(OrderRepository).Assembly,
            "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");
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
