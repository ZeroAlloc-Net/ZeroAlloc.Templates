using System.Net.Http.Headers;
using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using Npgsql;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

/// <summary>
/// Single-method WritePipeline benchmark for the za-clean Clean Architecture
/// template. Hosts the API via WebApplicationFactory&lt;Program&gt; and runs
/// POST /orders end-to-end through ASP.NET middleware, mediator dispatch,
/// validation, EF Core SaveChanges, and a stubbed shipping client.
///
/// <para>
/// <b>Backends:</b> <c>[Params]</c> dispatches the benchmark against both
/// in-memory SQLite and a localhost Postgres. Sqlite uses the production
/// schema path (<c>Program.cs</c>'s <c>ApplyEmbeddedSchemaAsync</c> reading
/// <c>schema.sql</c>); Postgres creates a fresh per-process database
/// (<c>bench_&lt;guid8&gt;</c>) and applies <c>schema.postgres.sql</c> via
/// the same path. Both code paths are AOT-correct (no EF reflection at runtime).
/// </para>
///
/// <para>
/// <b>Local dev — Postgres profile only:</b>
/// <code>
/// docker run --rm -d -p 5432:5432 \
///   -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
///   --name bench-pg postgres:17 \
///   -c max_connections=500
/// </code>
/// then <c>dotnet run -c Release -- --filter "*WritePipelineBench*"</c>.
/// CI provisions Postgres via the docker-run pattern in
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
    private object? _request;

    [GlobalSetup]
    public void Setup()
    {
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
                ["Database:Provider"] = Backend == DbBackend.Postgres ? "Postgres" : "Sqlite",
                ["Database:SchemaStrategy"] = "EmbeddedScript",
            }));
            b.ConfigureServices(s =>
            {
                // Strip all EF Core registrations the production AddDbContextPool
                // left behind. typeof(AppDbContext) catches the pool-flavored
                // factory descriptor (lives in MyApp.Infrastructure.Persistence
                // namespace, not Microsoft.EntityFrameworkCore.*). Per the
                // 4e0615f lesson from PR #140.
                var efDescriptors = s
                    .Where(d => d.ServiceType == typeof(AppDbContext)
                        || (d.ServiceType.FullName is { } n
                            && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                                || n.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal))))
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

                var shippingDescriptor = s.SingleOrDefault(d => d.ServiceType == typeof(IShippingQuoteClient));
                if (shippingDescriptor is not null)
                {
                    s.Remove(shippingDescriptor);
                }
                s.AddScoped<IShippingQuoteClient, BenchShippingClient>();
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        _request = new
        {
            customerId = 42,
            items = new[] { new { sku = "SKU-1", quantity = 2, unitPriceEur = 15m } },
            shippingZip = "1011AA",
        };
    }

    [Benchmark]
    public async Task<HttpResponseMessage> WritePipeline()
        => await _client!.PostAsJsonAsync("/orders", _request);

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

internal sealed class BenchShippingClient : IShippingQuoteClient
{
    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        => Task.FromResult(Money.TryCreate(5m, "EUR"));
}

public enum DbBackend
{
    Sqlite,
    Postgres,
}
