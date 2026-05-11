using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests;

public sealed class AuthorizationTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public AuthorizationTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task POST_orders_without_required_scope_returns_403()
    {
        var client = _factory.CreateClient();
        var tokenWithoutScope = TestJwt.Issue();  // no scope claim
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenWithoutScope);

        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = 42,
            items = new[] { new { sku = "SKU-1", quantity = 2, unitPriceEur = 15m } },
            shippingZip = "1011AA",
        });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
