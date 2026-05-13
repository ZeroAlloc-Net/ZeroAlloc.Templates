using ZeroAlloc.Authorization;

#pragma warning disable MA0048 // two policies intentionally co-located in one file

namespace MyApp.Application.Authorization;

/// <summary>
/// Grants access to read-side order operations (queries). Requires the
/// "orders.read" scope claim, mirroring the endpoint-level "OrdersRead"
/// ASP.NET policy in Program.cs.
/// </summary>
[AuthorizationPolicy("OrdersRead")]
public sealed class OrdersReadPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => HasScope(ctx, "orders.read");

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
/// ASP.NET policy in Program.cs.
/// </summary>
[AuthorizationPolicy("OrdersWrite")]
public sealed class OrdersWritePolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => OrdersReadPolicy.HasScope(ctx, "orders.write");
}
