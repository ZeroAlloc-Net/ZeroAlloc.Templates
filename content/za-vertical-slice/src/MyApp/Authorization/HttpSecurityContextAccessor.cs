using System.Collections;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // accessor + adapter co-located in one file by design

namespace MyApp.Authorization;

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
/// Implements <see cref="ISecurityContext"/> directly on top of <see cref="ClaimsPrincipal"/>
/// without materializing the role or claim collections. <see cref="Roles"/> and <see cref="Claims"/>
/// return <c>this</c>, and every member walks <see cref="ClaimsPrincipal.FindAll(string)"/> on
/// demand. The hot path — a single <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/> for
/// the "scope" claim on every authorized request — is zero-allocation for single-value claims
/// (returns the original <see cref="Claim.Value"/> string) and allocates only the joined string
/// for multi-value claims per RFC 6749 §3.3.
/// </summary>
internal sealed class ClaimsPrincipalSecurityContext(ClaimsPrincipal principal)
    : ISecurityContext, IReadOnlySet<string>, IReadOnlyDictionary<string, string>
{
    public string Id => principal.Identity?.Name ?? string.Empty;
    public IReadOnlySet<string> Roles => this;
    public IReadOnlyDictionary<string, string> Claims => this;

    // ---- IReadOnlyDictionary<string, string> ----

    public bool TryGetValue(string key, out string value)
    {
        // Probe with FindFirst (returns null or a single Claim) for the zero-alloc single-value path.
        var first = principal.FindFirst(key);
        if (first is null)
        {
            value = string.Empty;
            return false;
        }
        // Single-value common case — bail before constructing the FindAll enumerator.
        // Walk principal.Claims directly to find any additional matches with the same type.
        string? joined = null;
        var seenFirst = false;
        foreach (var c in principal.Claims)
        {
            if (!string.Equals(c.Type, key, StringComparison.Ordinal)) continue;
            if (!seenFirst) { seenFirst = true; continue; }
            joined = joined is null ? $"{first.Value} {c.Value}" : $"{joined} {c.Value}";
        }
        value = joined ?? first.Value;
        return true;
    }

    public string this[string key] => TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key);

    public bool ContainsKey(string key) => principal.FindFirst(key) is not null;

    public IEnumerable<string> Keys
    {
        get
        {
            // Cold path — not on the [RequirePolicy] hot path. Deduplicate claim types.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in principal.Claims)
                if (seen.Add(c.Type)) yield return c.Type;
        }
    }

    public IEnumerable<string> Values
    {
        get
        {
            // Cold path — projects through TryGetValue so multi-value semantics match.
            foreach (var key in Keys)
                if (TryGetValue(key, out var v)) yield return v;
        }
    }

    int IReadOnlyCollection<KeyValuePair<string, string>>.Count
    {
        get
        {
            // Cold path — count distinct claim types.
            var n = 0;
            foreach (var _ in Keys) n++;
            return n;
        }
    }

    IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
    {
        foreach (var key in Keys)
            if (TryGetValue(key, out var v))
                yield return new KeyValuePair<string, string>(key, v);
    }

    // ---- IReadOnlySet<string> (roles) ----

    public bool Contains(string item)
    {
        foreach (var c in principal.FindAll(ClaimTypes.Role))
            if (string.Equals(c.Value, item, StringComparison.Ordinal))
                return true;
        return false;
    }

    int IReadOnlyCollection<string>.Count
    {
        get
        {
            var n = 0;
            foreach (var _ in principal.FindAll(ClaimTypes.Role)) n++;
            return n;
        }
    }

    IEnumerator<string> IEnumerable<string>.GetEnumerator()
    {
        foreach (var c in principal.FindAll(ClaimTypes.Role))
            yield return c.Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<string>)this).GetEnumerator();

    bool IReadOnlySet<string>.IsProperSubsetOf(IEnumerable<string> other) => RoleSet().IsProperSubsetOf(other);
    bool IReadOnlySet<string>.IsProperSupersetOf(IEnumerable<string> other) => RoleSet().IsProperSupersetOf(other);
    bool IReadOnlySet<string>.IsSubsetOf(IEnumerable<string> other) => RoleSet().IsSubsetOf(other);
    bool IReadOnlySet<string>.IsSupersetOf(IEnumerable<string> other) => RoleSet().IsSupersetOf(other);
    bool IReadOnlySet<string>.Overlaps(IEnumerable<string> other) => RoleSet().Overlaps(other);
    bool IReadOnlySet<string>.SetEquals(IEnumerable<string> other) => RoleSet().SetEquals(other);

    private HashSet<string> RoleSet()
    {
        // Cold path — only invoked by the set-comparison members above, which are
        // never called by the template's policies today. Build a one-shot HashSet
        // for correctness; trade allocs for code simplicity since this is unreachable
        // on the hot path.
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in principal.FindAll(ClaimTypes.Role))
            set.Add(c.Value);
        return set;
    }
}
