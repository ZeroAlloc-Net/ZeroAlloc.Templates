using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

#pragma warning disable MA0048 // two policies intentionally co-located in one file

namespace MyApp.Application.Authorization;

/// <summary>Grants access to read-side order operations. Requires the "orders.read" scope.</summary>
[Policy("OrdersRead")]
public sealed class OrdersReadPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(HasScope(ctx, "orders.read")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.read scope"));

    /// <summary>
    /// Returns true if the space-separated "scope" claim contains <paramref name="scope"/>
    /// (RFC 6749 §3.3). Allocation-free — scans a ReadOnlySpan over the claim value.
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

/// <summary>Grants access to write-side order operations. Requires the "orders.write" scope.</summary>
[Policy("OrdersWrite")]
public sealed class OrdersWritePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(OrdersReadPolicy.HasScope(ctx, "orders.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.write scope"));
}
