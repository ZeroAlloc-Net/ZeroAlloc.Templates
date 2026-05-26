using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // accessor + adapter co-located in one file by design

namespace MyApp.Api.Authorization;

/// <summary>
/// Bridges the current <see cref="HttpContext.User"/> (a <see cref="ClaimsPrincipal"/>
/// populated by the JWT bearer middleware) into ZA.Authorization's <see cref="ISecurityContext"/>.
/// Registered via <c>UseAccessor&lt;HttpSecurityContextAccessor&gt;()</c> in Program.cs so
/// every mediator request gets the same identity the API endpoints see.
/// </summary>
public sealed class HttpSecurityContextAccessor(IHttpContextAccessor httpContextAccessor) : ISecurityContextAccessor
{
    public ISecurityContext Current => httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? new ClaimsPrincipalSecurityContext(user)
        : AnonymousSecurityContext.Instance;
}

/// <summary>
/// Wraps a <see cref="ClaimsPrincipal"/> as an <see cref="ISecurityContext"/>. Materialises
/// the role + claim views lazily on first access — handler dispatch on anonymous requests
/// pays only a null check.
/// </summary>
internal sealed class ClaimsPrincipalSecurityContext(ClaimsPrincipal principal) : ISecurityContext
{
    private IReadOnlySet<string>? _roles;
    private IReadOnlyDictionary<string, string>? _claims;

    public string Id => principal.Identity?.Name ?? string.Empty;

    public IReadOnlySet<string> Roles => _roles ??= MaterializeRoles(principal);

    public IReadOnlyDictionary<string, string> Claims => _claims ??= MaterializeClaims(principal);

    private static IReadOnlySet<string> MaterializeRoles(ClaimsPrincipal p)
    {
        var roles = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var c in p.FindAll(ClaimTypes.Role))
            roles.Add(c.Value);
        return roles;
    }

    private static IReadOnlyDictionary<string, string> MaterializeClaims(ClaimsPrincipal p)
    {
        var claims = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var c in p.Claims)
        {
            // Multi-value claims (e.g. multiple "scope" entries) get space-joined per RFC 6749 §3.3.
            if (claims.TryGetValue(c.Type, out var existing))
                claims[c.Type] = $"{existing} {c.Value}";
            else
                claims[c.Type] = c.Value;
        }
        return claims;
    }
}
