using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Infrastructure.Persistence;
using Xunit;

namespace MyApp.IntegrationTests;

public sealed class CreateOrderEndpointTests : IClassFixture<CreateOrderEndpointTests.Factory>
{
    private readonly Factory _factory;

    public CreateOrderEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task POST_orders_returns_201_with_jwt()
    {
        var client = _factory.CreateClient();
        var token = TestJwt.Issue(["orders.write"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            items = new[] { new { sku = "SKU-1", quantity = 2, unitPriceEur = 15m } },
            shippingZip = "1011AA",
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task POST_orders_without_jwt_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            items = Array.Empty<object>(),
            shippingZip = "1011AA",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>
    /// Hosts the API with two swaps: the EF DbContext is rebound to a shared,
    /// kept-alive SQLite in-memory connection, and IShippingQuoteClient is replaced
    /// by a deterministic stub. Migrations from MyApp.Infrastructure are applied
    /// against the in-memory database during fixture construction.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        // Connection is opened once per fixture and stays open for the test session;
        // SQLite's :memory: database is bound to the connection's lifetime.
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public Factory()
        {
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:DevSigningKey"] = TestJwt.DevKey,
                });
            });

            builder.ConfigureServices(services =>
            {
                // Replace the production DbContext registration with one bound to the
                // in-memory SQLite connection we keep alive for the fixture's lifetime.
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor is not null)
                {
                    services.Remove(dbDescriptor);
                }

                services.AddDbContext<AppDbContext>(opt =>
                {
                    opt.UseSqlite(_connection, sqlite =>
                        sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                    opt.ConfigureWarnings(w => w.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                });

                // Swap the real shipping client for a deterministic stub.
                var shippingDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IShippingQuoteClient));
                if (shippingDescriptor is not null)
                {
                    services.Remove(shippingDescriptor);
                }

                services.AddScoped<IShippingQuoteClient, TestShippingClient>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connection.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
