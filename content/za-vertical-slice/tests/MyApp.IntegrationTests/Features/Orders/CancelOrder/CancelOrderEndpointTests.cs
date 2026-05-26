using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests.Features.Orders.CancelOrder;

public sealed class CancelOrderEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public CancelOrderEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DELETE_orders_id_returns_204_for_pending_order()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        var createResp = await client.PostAsJsonAsync("/orders", new { customerId = 1, total = 10m });
        createResp.EnsureSuccessStatusCode();
        var location = createResp.Headers.Location!.ToString();

        var resp = await client.DeleteAsync(location);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DELETE_orders_id_returns_404_for_unknown_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        var resp = await client.DeleteAsync("/orders/9999");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
