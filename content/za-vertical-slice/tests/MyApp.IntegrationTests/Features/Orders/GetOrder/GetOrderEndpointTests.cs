using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApp.Common;
using MyApp.Features.Orders.GetOrder;
using Xunit;

namespace MyApp.IntegrationTests.Features.Orders.GetOrder;

public sealed class GetOrderEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public GetOrderEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_orders_id_returns_200_for_known_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        // Seed via the public POST path so the test exercises the same write
        // pipeline production callers use.
        var createResp = await client.PostAsJsonAsync("/orders", new { customerId = 42, total = 12.34m });
        createResp.EnsureSuccessStatusCode();
        var location = createResp.Headers.Location!.ToString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read"]));
        var resp = await client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(dto);
        Assert.Equal(new CustomerId(42), dto!.CustomerId);
        Assert.Equal(12.34m, dto.Total);
    }

    [Fact]
    public async Task GET_orders_id_returns_404_for_unknown_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read"]));

        var resp = await client.GetAsync("/orders/9999");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
