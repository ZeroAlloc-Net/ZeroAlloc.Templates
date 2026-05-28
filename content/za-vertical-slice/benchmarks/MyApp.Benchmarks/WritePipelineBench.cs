using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Npgsql;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

/// <summary>
/// Attribution benchmark for the PlaceOrder slice. Three measurements peel
/// the pipeline back one layer at a time so the cost of each layer falls
/// out as a delta:
/// <list type="bullet">
///   <item><description><c>PlaceOrder_FullPipeline</c> — HTTP → JWT → endpoint policy → mediator [RequirePolicy] → [Validate] → handler → EF.</description></item>
///   <item><description><c>PlaceOrder_MediatorDirect</c> — mediator [RequirePolicy] → [Validate] → handler → EF (HTTP + JWT bypassed; HttpContext pre-populated with the scope claim so authorization sees an authenticated principal).</description></item>
///   <item><description><c>PlaceOrder_HandlerDirect</c> — handler → EF (mediator, validation, authorization all bypassed; raw handler invocation against the scoped DbContext).</description></item>
/// </list>
/// <para>
/// <b>Reading the deltas:</b> (Full − MediatorDirect) is the cost of the HTTP
/// + JWT + JSON-deserialization layer. (MediatorDirect − HandlerDirect) is the
/// cost of mediator dispatch + validation pipeline + authorization pipeline.
/// HandlerDirect itself is the EF baseline.
/// </para>
/// <para>
/// <b>Backends:</b> <c>[Params]</c> dispatches each method against both
/// in-memory SQLite and a localhost Postgres. Sqlite uses the production
/// schema path (<c>Program.cs</c>'s <c>MigrateAsync</c>). Postgres uses
/// <c>EnsureCreated()</c> against a fresh per-process database
/// (<c>bench_&lt;guid8&gt;</c>) — existing Sqlite-typed migrations don't
/// translate, and the bench profile owns its schema path exclusively.
/// </para>
/// <para>
/// <b>Local dev — Postgres profile only:</b>
/// <code>
/// docker run --rm -d -p 5432:5432 \
///   -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
///   --name bench-pg postgres:17
/// </code>
/// then <c>dotnet run -c Release -- --filter "*WritePipelineBench*"</c>.
/// CI provisions Postgres via the <c>services:</c> block in
/// <c>.github/workflows/benchmarks.yml</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class WritePipelineBench
{
    [Params(DbBackend.Sqlite, DbBackend.Postgres)]
    public DbBackend Backend { get; set; }

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private SqliteConnection? _connection;
    private string? _postgresAdminConnString;
    private string? _postgresDbName;
    private object? _httpRequest;
    private PlaceOrderCommand _command;

    [GlobalSetup]
    public void Setup()
    {
        var skipMigrate = Backend == DbBackend.Postgres;
        NpgsqlConnectionStringBuilder? csb = null;

        if (Backend == DbBackend.Sqlite)
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }
        else
        {
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
            var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
            var pwd = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
            var adminDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bench";

            csb = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.Parse(port, System.Globalization.CultureInfo.InvariantCulture),
                Username = user,
                Password = pwd,
                Database = adminDb,
            };
            _postgresAdminConnString = csb.ConnectionString;
            _postgresDbName = "bench_" + Guid.NewGuid().ToString("N")[..8];

            using var admin = new NpgsqlConnection(_postgresAdminConnString);
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_postgresDbName}\"";
            cmd.ExecuteNonQuery();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
                ["Bench:SkipStartupMigrate"] = skipMigrate ? "true" : "false",
            }));
            b.ConfigureServices(s =>
            {
                // Strip every EF Core registration the production AddDbContext
                // left behind. Removing only DbContextOptions<AppDbContext>
                // leaves provider-specific singletons (IDatabaseProvider,
                // IRelationalConnection, etc.) from the original UseSqlite
                // call in the container; re-adding AddDbContext with UseNpgsql
                // then registers a second IDatabaseProvider and EF throws
                // "Services for database providers '...Sqlite', '...Npgsql'
                // have been registered". Removing the whole EF stack
                // unconditionally is simpler than a per-provider strip and
                // is correct for the Sqlite branch too (re-adds an identical
                // stack).
                var efDescriptors = s
                    .Where(d => d.ServiceType.FullName is { } n
                        && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                            || n.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal)))
                    .ToList();
                foreach (var d in efDescriptors)
                {
                    s.Remove(d);
                }

                if (Backend == DbBackend.Sqlite)
                {
                    s.AddDbContext<AppDbContext>(opt =>
                    {
                        opt.UseSqlite(_connection!, sqlite =>
                            sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                        opt.ConfigureWarnings(w => w.Ignore(
                            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    });
                }
                else
                {
                    csb!.Database = _postgresDbName;
                    var workerConnString = csb.ConnectionString;
                    s.AddDbContext<AppDbContext>(opt =>
                    {
                        opt.UseNpgsql(workerConnString);
                        opt.ConfigureWarnings(w => w.Ignore(
                            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    });
                }
            });
        });

        if (Backend == DbBackend.Postgres)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        _httpRequest = new { customerId = 42, total = 99.99m };
        _command = new PlaceOrderCommand(CustomerId: new CustomerId(42), Total: 99.99m);
    }

    /// <summary>Full HTTP path: serialization + JWT validation + ASP.NET policy + mediator + EF.</summary>
    [Benchmark(Baseline = true)]
    public async Task<HttpResponseMessage> PlaceOrder_FullPipeline()
        => await _client!.PostAsJsonAsync("/orders", _httpRequest);

    /// <summary>
    /// In-process mediator dispatch. Bypasses HTTP and JWT but still pays the
    /// mediator [RequirePolicy] + [Validate] pipeline behaviors. HttpContext is
    /// pre-populated with the scope claim so HttpSecurityContextAccessor returns
    /// an authenticated principal — otherwise the policy denies the request and
    /// the benchmark measures the authorization-failure path instead of the
    /// happy path.
    /// </summary>
    [Benchmark]
    public async Task<Result<OrderId, Error>> PlaceOrder_MediatorDirect()
    {
        using var scope = _factory!.Services.CreateScope();
        SeedHttpContext(scope.ServiceProvider);
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(_command, CancellationToken.None);
    }

    /// <summary>Raw handler call. Bypasses mediator, validation, and authorization entirely.</summary>
    [Benchmark]
    public async Task<Result<OrderId, Error>> PlaceOrder_HandlerDirect()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new PlaceOrderHandler(db);
        return await handler.Handle(_command, CancellationToken.None);
    }

    private static void SeedHttpContext(IServiceProvider sp)
    {
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("scope", "orders.write"),
            new Claim("sub", "bench"),
        }, authenticationType: "Bearer");
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _connection?.Dispose();

        if (Backend == DbBackend.Postgres && _postgresAdminConnString is not null && _postgresDbName is not null)
        {
            try
            {
                using var admin = new NpgsqlConnection(_postgresAdminConnString);
                admin.Open();
                using var cmd = admin.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_postgresDbName}\" WITH (FORCE)";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Best-effort cleanup. CI containers are ephemeral; local devs
                // can drop stale bench_* databases manually if a process crashes.
            }
        }
    }
}

public enum DbBackend
{
    Sqlite,
    Postgres,
}
