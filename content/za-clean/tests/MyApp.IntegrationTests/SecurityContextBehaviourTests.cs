using System.Collections;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyApp.Api.Authorization;
using Xunit;
using ZeroAlloc.Authorization;

namespace MyApp.IntegrationTests;

/// <summary>
/// What the ClaimsPrincipal -> ISecurityContext bridge returns. Policies an adopter writes read
/// identity through <see cref="ISecurityContext.Claims"/> and <see cref="ISecurityContext.Roles"/>,
/// so their semantics are tested here, not only the allocation-free lookups.
/// </summary>
public sealed class SecurityContextBehaviourTests
{
    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    private static HttpSecurityContextAccessor Accessor(HttpContext? context)
        => new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Accessor_returns_anonymous_without_a_request()
    {
        Assert.Same(AnonymousSecurityContext.Instance, Accessor(null).Current);
    }

    [Fact]
    public void Accessor_returns_anonymous_for_an_unauthenticated_user()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        Assert.Same(AnonymousSecurityContext.Instance, Accessor(context).Current);
    }

    [Fact]
    public void Id_is_empty_when_the_identity_has_no_name()
    {
        Assert.Equal(string.Empty, new ClaimsPrincipalSecurityContext(Authenticated()).Id);
    }

    [Fact]
    public void Claims_index_by_type_and_report_missing_keys()
    {
        var claims = new ClaimsPrincipalSecurityContext(Authenticated(new Claim("sub", "alice"))).Claims;

        Assert.Equal("alice", claims["sub"]);
        Assert.True(claims.ContainsKey("sub"));

        Assert.False(claims.TryGetValue("missing", out var missing));
        Assert.Equal(string.Empty, missing);
        Assert.False(claims.ContainsKey("missing"));
        Assert.Throws<KeyNotFoundException>(() => claims["missing"]);
    }

    [Fact]
    public void Claims_enumerate_each_claim_type_once_through_every_enumeration_interface()
    {
        var claims = new ClaimsPrincipalSecurityContext(Authenticated(
            new Claim("scope", "orders.read"),
            new Claim("scope", "orders.write"),
            new Claim("sub", "alice"))).Claims;

        Assert.Equal(["scope", "sub"], claims.Keys);
        Assert.Equal(["orders.read orders.write", "alice"], claims.Values);
        Assert.Equal(2, claims.Count);
        KeyValuePair<string, string>[] expected =
            [new("scope", "orders.read orders.write"), new("sub", "alice")];
        Assert.Equal(expected, claims.ToArray());
        // Non-generic enumeration, as xUnit's collection asserts and reflection-based
        // serializers use, must also yield the claims. It used to yield the roles, because one
        // object implemented both collections.
        var untyped = new List<object>();
        foreach (var item in (IEnumerable)claims)
            untyped.Add(item);
        Assert.Equal(expected.Cast<object>(), untyped);
    }

    [Fact]
    public void Roles_are_the_role_claims()
    {
        var roles = new ClaimsPrincipalSecurityContext(Authenticated(
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, "auditor"),
            new Claim("scope", "admin"))).Roles;

        Assert.Contains("admin", roles);
        Assert.DoesNotContain("orders.read", roles);
        Assert.Equal(2, roles.Count);
        Assert.Equal(["admin", "auditor"], roles);
        Assert.Equal(2, ((IEnumerable)roles).Cast<string>().Count());

        Assert.True(roles.SetEquals(["auditor", "admin"]));
        Assert.True(roles.IsSubsetOf(["admin", "auditor", "user"]));
        Assert.True(roles.IsProperSubsetOf(["admin", "auditor", "user"]));
        Assert.True(roles.IsSupersetOf(["admin"]));
        Assert.True(roles.IsProperSupersetOf(["admin"]));
        Assert.True(roles.Overlaps(["user", "admin"]));
        Assert.False(roles.Overlaps(["user"]));
    }

    [Fact]
    public void Identities_and_claims_that_are_not_lists_are_walked_too()
    {
        var context = new ClaimsPrincipalSecurityContext(new StreamedPrincipal(
            new StreamedIdentity(new Claim("scope", "orders.read"), new Claim("scope", "orders.write"))));

        Assert.Equal("orders.read orders.write", context.Claims["scope"]);
        Assert.True(context.Claims.ContainsKey("scope"));
        Assert.False(context.Claims.ContainsKey("missing"));
    }

    // Expose identities and claims as iterators rather than lists, as a custom principal may.
    private sealed class StreamedPrincipal(ClaimsIdentity identity) : ClaimsPrincipal(identity)
    {
        public override IEnumerable<ClaimsIdentity> Identities => Stream(base.Identities);
    }

    private sealed class StreamedIdentity(params Claim[] claims) : ClaimsIdentity(claims, "test")
    {
        public override IEnumerable<Claim> Claims => Stream(base.Claims);
    }

    private static IEnumerable<T> Stream<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
            yield return item;
    }
}
