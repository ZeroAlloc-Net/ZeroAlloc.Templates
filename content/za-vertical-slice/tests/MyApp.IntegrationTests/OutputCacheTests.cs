using System.Data;
using System.Data.Async;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Cache behavior tests for the OutputCache layer (#189). Each test uses a
/// side-channel SQL UPDATE through the factory's IAsyncDbConnection to mutate
/// the underlying row, then re-issues the GET to observe whether the cache
/// served the stale body (HIT) or read the new body (MISS / evicted).
///
/// Mirrors za-clean/OutputCacheTests.cs but adapted to vs's two cached
/// entities — Orders and Customers — and vs's flat command shapes
/// (<c>PlaceOrderCommand(CustomerId, Total)</c>,
/// <c>CreateCustomerCommand(Name, Email)</c>).
///
/// Each test creates its own factory so the in-memory cache state is isolated.
/// </summary>
public sealed class OutputCacheTests
{
    private static MyAppFactoryWithCacheTtl CreateFactory(int ttlSeconds = 30) =>
        new MyAppFactoryWithCacheTtl(ttlSeconds);

    // ============================================================
    // Orders
    // ============================================================

    [Fact]
    public async Task GET_orders_id_serves_cached_body_when_called_twice()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var seededId = await SeedOneOrderAsync(client);

        var first = await client.GetAsync($"/orders/{seededId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        var second = await client.GetAsync($"/orders/{seededId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);
        Assert.DoesNotContain("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task POST_orders_evicts_cached_GET_responses_so_next_read_is_fresh()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var seededId = await SeedOneOrderAsync(client);

        var first = await client.GetAsync($"/orders/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        // A POST to /orders evicts ALL "orders"-tagged cache entries.
        await SeedOneOrderAsync(client);

        var second = await client.GetAsync($"/orders/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task GET_orders_id_serves_fresh_body_after_TTL_expiry()
    {
        using var factory = CreateFactory(ttlSeconds: 1);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var seededId = await SeedOneOrderAsync(client);
        var first = await client.GetAsync($"/orders/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        // Wait past TTL with slack so we're not racing the timer.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var second = await client.GetAsync($"/orders/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task GET_orders_with_different_ids_do_not_share_cache_entries()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var id1 = await SeedOneOrderAsync(client, customerId: 1);
        var id2 = await SeedOneOrderAsync(client, customerId: 2);

        var resp1 = await client.GetAsync($"/orders/{id1}");
        var resp2 = await client.GetAsync($"/orders/{id2}");
        var body1 = await resp1.Content.ReadAsStringAsync();
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Contains("\"customerId\":1", body1);
        Assert.Contains("\"customerId\":2", body2);
        Assert.NotEqual(body1, body2);
    }

    // ============================================================
    // Customers
    // ============================================================

    [Fact]
    public async Task GET_customers_id_serves_cached_body_when_called_twice()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read", "customers.write"]));

        var seededId = await SeedOneCustomerAsync(client, name: "Alice", email: "alice@example.com");

        var first = await client.GetAsync($"/customers/{seededId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateCustomerNameAsync(factory, seededId, newName: "Mutated");

        var second = await client.GetAsync($"/customers/{seededId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);
        Assert.DoesNotContain("\"name\":\"Mutated\"", secondBody);
    }

    [Fact]
    public async Task POST_customers_evicts_cached_GET_responses_so_next_read_is_fresh()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read", "customers.write"]));

        var seededId = await SeedOneCustomerAsync(client, name: "Alice", email: "alice@example.com");

        var first = await client.GetAsync($"/customers/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateCustomerNameAsync(factory, seededId, newName: "Mutated");

        // A POST to /customers evicts ALL "customers"-tagged cache entries.
        await SeedOneCustomerAsync(client, name: "Bob", email: "bob@example.com");

        var second = await client.GetAsync($"/customers/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"name\":\"Mutated\"", secondBody);
    }

    [Fact]
    public async Task GET_customers_id_serves_fresh_body_after_TTL_expiry()
    {
        using var factory = CreateFactory(ttlSeconds: 1);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read", "customers.write"]));

        var seededId = await SeedOneCustomerAsync(client, name: "Alice", email: "alice@example.com");
        var first = await client.GetAsync($"/customers/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateCustomerNameAsync(factory, seededId, newName: "Mutated");

        // Wait past TTL with slack so we're not racing the timer.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var second = await client.GetAsync($"/customers/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"name\":\"Mutated\"", secondBody);
    }

    [Fact]
    public async Task GET_customers_with_different_ids_do_not_share_cache_entries()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read", "customers.write"]));

        var id1 = await SeedOneCustomerAsync(client, name: "Alice", email: "alice@example.com");
        var id2 = await SeedOneCustomerAsync(client, name: "Bob", email: "bob@example.com");

        var resp1 = await client.GetAsync($"/customers/{id1}");
        var resp2 = await client.GetAsync($"/customers/{id2}");
        var body1 = await resp1.Content.ReadAsStringAsync();
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Contains("\"name\":\"Alice\"", body1);
        Assert.Contains("\"name\":\"Bob\"", body2);
        Assert.NotEqual(body1, body2);
    }

    // ============================================================
    // Order helpers
    // ============================================================

    private static async Task<int> SeedOneOrderAsync(HttpClient client, int customerId = 42)
    {
        // vs's PlaceOrderCommand is the flat shape (CustomerId, Total) —
        // no nested items[] or shippingZip like za-clean's OrderRequest.
        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId,
            total = 99.99m,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var location = resp.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("POST /orders did not return a Location header");
        return int.Parse(location.Split('/')[^1]);
    }

    private static async Task UpdateOrderCustomerIdAsync(MyAppFactory factory, int orderId, int newCustomerId)
    {
        using var scope = factory.Services.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredService<IAsyncDbConnection>();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync().ConfigureAwait(false);
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Orders\" SET \"CustomerId\" = @cid WHERE \"Id\" = @id";
        var pCid = cmd.CreateParameter();
        pCid.ParameterName = "@cid";
        pCid.Value = newCustomerId;
        cmd.Parameters.Add(pCid);
        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        pId.Value = orderId;
        cmd.Parameters.Add(pId);
        var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        Assert.Equal(1, affected);
    }

    // ============================================================
    // Customer helpers
    // ============================================================

    private static async Task<int> SeedOneCustomerAsync(HttpClient client, string name, string email)
    {
        var resp = await client.PostAsJsonAsync("/customers", new
        {
            name,
            email,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var location = resp.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("POST /customers did not return a Location header");
        return int.Parse(location.Split('/')[^1]);
    }

    private static async Task UpdateCustomerNameAsync(MyAppFactory factory, int customerId, string newName)
    {
        using var scope = factory.Services.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredService<IAsyncDbConnection>();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync().ConfigureAwait(false);
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = @name WHERE \"Id\" = @id";
        var pName = cmd.CreateParameter();
        pName.ParameterName = "@name";
        pName.Value = newName;
        cmd.Parameters.Add(pName);
        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        pId.Value = customerId;
        cmd.Parameters.Add(pId);
        var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        Assert.Equal(1, affected);
    }
}

/// <summary>
/// Factory variant that overrides both <c>OutputCache:OrderByIdTtlSeconds</c>
/// and <c>OutputCache:CustomerByIdTtlSeconds</c> for the TTL-expiry tests. A
/// single TTL is applied to both keys for simplicity (each TTL test uses only
/// one endpoint anyway). Reuses MyAppFactory's connection/seed wiring otherwise.
/// </summary>
internal sealed class MyAppFactoryWithCacheTtl : MyAppFactory
{
    private readonly int _ttl;

    public MyAppFactoryWithCacheTtl(int ttlSeconds)
    {
        _ttl = ttlSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting() is the only override path that survives WebApplication's
        // minimal-host bootstrap reliably under WebApplicationFactory<Program>:
        // ConfigureAppConfiguration callbacks run during the host-builder pass
        // but the WebApplicationBuilder owns its own configuration assembled at
        // CreateBuilder() time, before those callbacks fire. UseSetting writes
        // directly to the builder's host-configuration dictionary, which IS
        // visible to builder.Configuration.GetValue inside Program.cs.
        builder.UseSetting("OutputCache:OrderByIdTtlSeconds", _ttl.ToString());
        builder.UseSetting("OutputCache:CustomerByIdTtlSeconds", _ttl.ToString());
        base.ConfigureWebHost(builder);
    }
}
