using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyApp.Api.Authorization;
using Xunit;

// Pure in-memory tests for the ClaimsPrincipal -> ISecurityContext bridge. Lives in
// IntegrationTests because that is the test project that references MyApp.Api.
namespace MyApp.IntegrationTests;

public class SecurityContextAllocationTests
{
    private const int Iterations = 1_000;

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void Single_value_scope_lookup_allocates_nothing()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal(
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("scope", "orders.read orders.write")));

        Assert.Equal(0, AllocatedOver(() => ctx.TryGetValue("scope", out _)));
    }

    [Fact]
    public void Missing_claim_lookup_allocates_nothing()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal(new Claim(ClaimTypes.Name, "alice")));

        Assert.Equal(0, AllocatedOver(() => ctx.TryGetValue("scope", out _)));
    }

    [Fact]
    public void Multi_value_claims_are_joined_with_a_space()
    {
        var ctx = new ClaimsPrincipalSecurityContext(Principal(
            new Claim("scope", "orders.read"),
            new Claim("scope", "orders.write")));

        Assert.True(ctx.TryGetValue("scope", out var scopes));
        Assert.Equal("orders.read orders.write", scopes);
    }

    [Fact]
    public void Claims_across_identities_are_found()
    {
        var principal = new ClaimsPrincipal(new[]
        {
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "first"),
            new ClaimsIdentity(new[] { new Claim("scope", "orders.read"), new Claim(ClaimTypes.Role, "admin") }, "second"),
        });
        var ctx = new ClaimsPrincipalSecurityContext(principal);

        Assert.True(ctx.TryGetValue("scope", out var scopes));
        Assert.Equal("orders.read", scopes);
        Assert.True(ctx.ContainsKey("scope"));
        Assert.True(ctx.Roles.Contains("admin"));
        Assert.False(ctx.Roles.Contains("guest"));
    }

    [Fact]
    public void Current_is_cached_for_the_request()
    {
        var http = new DefaultHttpContext { User = Principal(new Claim("scope", "orders.read")) };
        var accessor = new HttpSecurityContextAccessor(new HttpContextAccessor { HttpContext = http });

        var first = accessor.Current;
        Assert.Same(first, accessor.Current);
        Assert.Equal(0, AllocatedOver(() => _ = accessor.Current));
    }

    [Fact]
    public void Current_follows_a_replaced_principal()
    {
        var http = new DefaultHttpContext { User = Principal(new Claim("scope", "orders.read")) };
        var accessor = new HttpSecurityContextAccessor(new HttpContextAccessor { HttpContext = http });
        var first = accessor.Current;

        http.User = Principal(new Claim("scope", "orders.write"));

        Assert.NotSame(first, accessor.Current);
        Assert.True(accessor.Current.Claims.TryGetValue("scope", out var scopes));
        Assert.Equal("orders.write", scopes);
    }

    // Total bytes allocated by Iterations calls after warm-up; must be exactly 0.
    private static long AllocatedOver(Action action)
    {
        for (var i = 0; i < 100; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
