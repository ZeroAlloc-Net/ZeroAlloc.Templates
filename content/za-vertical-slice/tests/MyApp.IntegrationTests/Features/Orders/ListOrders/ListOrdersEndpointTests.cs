using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApp.Features.Orders.ListOrders;
using Xunit;

namespace MyApp.IntegrationTests.Features.Orders.ListOrders;

public sealed class ListOrdersEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public ListOrdersEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_orders_with_orders_read_scope_returns_page()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.read"]));

        var resp = await client.GetAsync("/orders?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<OrderPage>();
        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        Assert.Equal(10, page.PageSize);
    }

    [Fact]
    public async Task GET_orders_without_jwt_returns_401()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/orders?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
