using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests.Features.Orders.PlaceOrder;

/// <summary>
/// HTTP-level tests for the PlaceOrder slice. Exercises the full pipeline:
/// JWT auth → endpoint policy → mediator [RequirePolicy] → [Validate] →
/// handler → EF Core SaveChanges → 201 Created with Location header.
/// </summary>
public sealed class PlaceOrderEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public PlaceOrderEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task POST_orders_with_valid_body_and_scope_returns_201()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            total = 99.99m,
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.StartsWith("/orders/", resp.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task POST_orders_without_jwt_returns_401()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            total = 99.99m,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
