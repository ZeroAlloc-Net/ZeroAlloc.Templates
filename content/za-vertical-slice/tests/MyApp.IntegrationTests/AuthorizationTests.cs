using System.Security.Claims;
using MyApp.Authorization;
using Xunit;

namespace MyApp.IntegrationTests;

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
