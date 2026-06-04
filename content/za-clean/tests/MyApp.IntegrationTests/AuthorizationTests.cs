using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using MyApp.Api.Authorization;
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

public sealed class ClaimsPrincipalSecurityContextTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void Claims_TryGetValue_returns_single_value_unchanged()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal(("scope", "orders.read orders.write")));
        Assert.True(ctx.Claims.TryGetValue("scope", out var value));
        Assert.Equal("orders.read orders.write", value);
    }

    [Fact]
    public void Claims_TryGetValue_joins_multi_value_claims_with_space()
    {
        // RFC 6749 §3.3 — two separate "scope" claims must space-join into one string.
        var ctx = new ClaimsPrincipalSecurityContext(Principal(
            ("scope", "orders.read"),
            ("scope", "orders.write")));
        Assert.True(ctx.Claims.TryGetValue("scope", out var value));
        Assert.Equal("orders.read orders.write", value);
    }

    [Fact]
    public void Claims_TryGetValue_missing_key_returns_false()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal(("sub", "user-1")));
        Assert.False(ctx.Claims.TryGetValue("scope", out var value));
        Assert.True(string.IsNullOrEmpty(value));
    }

    [Fact]
    public void Roles_Contains_hits_existing_role()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal((ClaimTypes.Role, "admin")));
        Assert.Contains("admin", ctx.Roles);
    }

    [Fact]
    public void Roles_Contains_misses_unknown_role()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal((ClaimTypes.Role, "admin")));
        Assert.DoesNotContain("guest", ctx.Roles);
    }

    [Fact]
    public void Id_returns_principal_identity_name_or_empty()
    {
        var withName = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "test"));
        var withoutName = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Equal("alice", new ClaimsPrincipalSecurityContext(withName).Id);
        Assert.Equal(string.Empty, new ClaimsPrincipalSecurityContext(withoutName).Id);
    }
}
