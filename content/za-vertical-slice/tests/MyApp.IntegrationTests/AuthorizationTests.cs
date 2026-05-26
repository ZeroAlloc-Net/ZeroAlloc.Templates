using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests;

// Authorization is enforced at TWO layers (defense in depth):
//
//   1. Endpoint level — `.RequireAuthorization("OrdersRead" / "OrdersWrite")` on
//      the minimal-API endpoints in OrdersEndpoints.cs. ASP.NET returns 401/403
//      before any handler runs.
//
//   2. Mediator level — [Authorize("OrdersRead")] / [Authorize("OrdersWrite")]
//      on the IRequest<T> records. ZA.Mediator.Authorization's pipeline behavior
//      consults the registered policy plus the ambient ISecurityContext (bridged
//      from HttpContext.User via HttpSecurityContextAccessor). Even if a future
//      endpoint forgets RequireAuthorization, the handler refuses to dispatch.
//
// These tests assert the endpoint-layer behavior end-to-end. The mediator-layer
// behavior is unit-tested upstream in ZA.Mediator.Authorization.
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
