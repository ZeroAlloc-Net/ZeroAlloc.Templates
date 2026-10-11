using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests;

public sealed class CreateOrderEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public CreateOrderEndpointTests(MyAppFactory factory)
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
    public async Task POST_orders_with_invalid_body_returns_400()
    {
        // Empty items and a malformed zip must be rejected by validation before
        // anything is persisted.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            items = Array.Empty<object>(),
            shippingZip = "not-a-zip",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
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
}
