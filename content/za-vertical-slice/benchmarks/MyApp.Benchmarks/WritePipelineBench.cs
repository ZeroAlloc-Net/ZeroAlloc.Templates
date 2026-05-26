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
/// HandlerDirect itself is the EF baseline — Add + SaveChanges through the
/// value-converted typed-id keys.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class WritePipelineBench
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private SqliteConnection? _connection;
    private object? _httpRequest;
    private PlaceOrderCommand _command;

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

        _httpRequest = new { customerId = 42, total = 99.99m };
        _command = new PlaceOrderCommand(CustomerId: 42, Total: 99.99m);
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
    }
}
