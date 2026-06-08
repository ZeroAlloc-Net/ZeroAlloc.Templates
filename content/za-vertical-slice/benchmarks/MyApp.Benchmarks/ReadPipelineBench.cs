using System.Data.Async;
using System.Data.Async.Adapters;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.Benchmarks;

/// <summary>
/// Single-method ReadPipeline benchmark for the za-vertical-slice template.
/// Hosts the API via WebApplicationFactory&lt;Program&gt; and runs GET /orders/{id}
/// end-to-end through ASP.NET middleware, JWT auth, mediator dispatch, the
/// ZA.ORM-generated [Query] partial in GetOrderHandler, and JSON response
/// serialisation.
///
/// <para>
/// <b>Scope:</b> SQLite only — the goal is to isolate the per-request
/// HTTP-layer cost (middleware + JSON + JWT + mediator), not the database
/// driver. The companion in-process MediatorDispatchBench (#189) measures
/// the mediator layer in isolation; subtracting the two localises the gap
/// to the HTTP stack.
/// </para>
///
/// <para>
/// One Order is seeded via POST /orders during [GlobalSetup]. Each
/// benchmark iteration issues a single GET on that id with a Bearer token
/// carrying the <c>orders.read</c> scope. The benchmark asserts a 2xx but
/// does NOT deserialise the body — measuring SUT cost, not test-client
/// cost. Any Kestrel-side serialisation IS captured (it's part of the SUT).
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ReadPipelineBench
{
    [Params(DbBackend.Sqlite)]
    public DbBackend Backend { get; set; }

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private SqliteConnection? _sqliteConn;
    private IAsyncDbConnection? _sqliteAsync;
    private int _seededId;
    private string? _getUrl;

    [GlobalSetup]
    public void Setup()
    {
        _sqliteConn = new SqliteConnection("DataSource=:memory:");
        _sqliteConn.Open();
        _sqliteAsync = _sqliteConn.AsAsync();
        ApplyMigrations(_sqliteAsync);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
                ["Database:Provider"] = "Sqlite",
                ["Database:SchemaStrategy"] = "Skip",
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
            }));
            b.ConfigureServices(s =>
            {
                var existing = s.SingleOrDefault(d => d.ServiceType == typeof(IAsyncDbConnection));
                if (existing is not null)
                {
                    s.Remove(existing);
                }

                // Singleton — see WritePipelineBench rationale (scoped lifetime
                // closes the underlying :memory: SqliteConnection after the
                // first request scope disposes).
                s.AddSingleton<IAsyncDbConnection>(_ => _sqliteAsync!);
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        // Seed via the same POST endpoint scenarios use; the vs schema has no
        // OrderLines table, so PlaceOrderCommand carries only customerId+total.
        var seedRequest = new
        {
            customerId = 42,
            total = 99.99m,
        };
        var postResponse = _client.PostAsJsonAsync("/orders", seedRequest).GetAwaiter().GetResult();
        postResponse.EnsureSuccessStatusCode();
        // vs returns the bare OrderId (typed-ID serialises as a bare integer).
        _seededId = postResponse.Content.ReadFromJsonAsync<int>().GetAwaiter().GetResult();
        _getUrl = $"/orders/{_seededId}";
    }

    [Benchmark]
    public async Task<HttpResponseMessage> ReadPipeline()
    {
        var response = await _client!.GetAsync(_getUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _sqliteConn?.Dispose();
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
