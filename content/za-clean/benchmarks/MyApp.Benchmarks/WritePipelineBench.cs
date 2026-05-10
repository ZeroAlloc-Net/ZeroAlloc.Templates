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
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

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
    }
}

internal sealed class BenchShippingClient : IShippingQuoteClient
{
    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        => Task.FromResult(Money.TryCreate(5m, "EUR"));
}
