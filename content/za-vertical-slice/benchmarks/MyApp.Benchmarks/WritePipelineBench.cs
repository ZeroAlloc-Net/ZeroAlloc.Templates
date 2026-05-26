using System.Net.Http.Headers;
using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Persistence;

namespace MyApp.Benchmarks;

/// <summary>
/// End-to-end write-path benchmark for the vertical-slice template's
/// PlaceOrder slice. Exercises the full HTTP pipeline (JwtBearer →
/// endpoint policy → mediator [RequirePolicy] → [Validate] → handler →
/// EF SaveChanges) against a kept-alive in-memory SQLite database, so
/// regressions in any of those layers surface as a per-call allocation
/// or duration delta.
/// </summary>
[MemoryDiagnoser]
public class WritePipelineBench
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private SqliteConnection? _connection;
    private object? _request;

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
            }));
            b.ConfigureServices(s =>
            {
                var dbDescriptor = s.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor is not null)
                {
                    s.Remove(dbDescriptor);
                }

                s.AddDbContext<AppDbContext>(opt =>
                {
                    opt.UseSqlite(_connection!, sqlite =>
                        sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                    opt.ConfigureWarnings(w => w.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                });

                using var scope = s.BuildServiceProvider().CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        // Vertical-slice PlaceOrderCommand: flat {customerId, total}, no nested
        // items or shipping zip — those belong to a richer slice when needed.
        _request = new
        {
            customerId = 42,
            total = 99.99m,
        };
    }

    [Benchmark]
    public async Task<HttpResponseMessage> PlaceOrder_VerticalSlice()
        => await _client!.PostAsJsonAsync("/orders", _request);

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _connection?.Dispose();
    }
}
