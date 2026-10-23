using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// The "OrdersRead" endpoint policy accepts orders.read or orders.write, so a writer can also
/// read. The mediator-level OrdersRead policy must agree, or a writer-only token gets 403 on an
/// uncached read and 200 on a cached one.
/// </summary>
public sealed class ReadScopeTests
{
    [Theory]
    [InlineData("orders.read")]
    [InlineData("orders.write")]
    public async Task GET_orders_id_is_allowed_for_either_order_scope_with_the_cache_off(string scope)
    {
        using var factory = new MyAppFactoryWithCacheTtl(ttlSeconds: 0);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.write"]));
        var created = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            items = new[] { new { sku = "SKU-A", quantity = 1, unitPriceEur = 10m } },
            shippingZip = "1011AA",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue([scope]));
        var read = await client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
