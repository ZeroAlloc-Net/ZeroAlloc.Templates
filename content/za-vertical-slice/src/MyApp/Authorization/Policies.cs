using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

#pragma warning disable MA0048 // multiple policies intentionally co-located in one file

namespace MyApp.Authorization;

/// <summary>
/// Grants access to read-side order operations (queries). Accepts the
/// "orders.read" or "orders.write" scope, so a writer can also read, matching
/// the endpoint-level "OrdersRead" ASP.NET policy in Program.cs. If the two
/// disagreed, a writer-only token would be refused on an uncached read and
/// allowed on a cached one.
/// </summary>
[Policy("OrdersRead")]
public sealed class OrdersReadPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(HasScope(ctx, "orders.read") || HasScope(ctx, "orders.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.read or orders.write scope"));

    /// <summary>
    /// Returns true if the space-separated "scope" claim contains <paramref name="scope"/>
    /// (RFC 6749 §3.3 token format). Allocation-free — scans a ReadOnlySpan over the claim
    /// value and compares each token against the expected scope without splitting.
    /// </summary>
    internal static bool HasScope(ISecurityContext ctx, string scope)
    {
        if (!ctx.Claims.TryGetValue("scope", out var scopes))
            return false;
        var span = scopes.AsSpan();
        while (span.Length > 0)
        {
            var nextSpace = span.IndexOf(' ');
            var token = nextSpace < 0 ? span : span[..nextSpace];
            if (token.SequenceEqual(scope.AsSpan()))
                return true;
            if (nextSpace < 0) break;
            span = span[(nextSpace + 1)..];
        }
        return false;
    }
}

/// <summary>
/// Grants access to write-side order operations (commands). Requires the
/// "orders.write" scope claim, mirroring the endpoint-level "OrdersWrite"
/// ASP.NET policy in Program.cs. The matching endpoint-level policy is strict:
/// only "orders.write" passes; the mediator-level policy here is intentionally
/// the same shape so the two layers agree on the same identity surface.
/// </summary>
[Policy("OrdersWrite")]
public sealed class OrdersWritePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(OrdersReadPolicy.HasScope(ctx, "orders.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.write scope"));
}

/// <summary>
/// Grants access to read-side customer queries. Accepts the "customers.read" or
/// "customers.write" scope, matching the endpoint-level "CustomersRead" policy.
/// Mirrors <see cref="OrdersReadPolicy"/> for the customers area.
/// </summary>
[Policy("CustomersRead")]
public sealed class CustomersReadPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(OrdersReadPolicy.HasScope(ctx, "customers.read") || OrdersReadPolicy.HasScope(ctx, "customers.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing customers.read or customers.write scope"));
}

/// <summary>
/// Grants access to write-side customer commands. Strict "customers.write" scope.
/// </summary>
[Policy("CustomersWrite")]
public sealed class CustomersWritePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(OrdersReadPolicy.HasScope(ctx, "customers.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing customers.write scope"));
}
