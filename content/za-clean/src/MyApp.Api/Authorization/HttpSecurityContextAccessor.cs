using System.Collections;
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
/// <para>
/// Registered scoped, so one instance serves one request. The security context it builds is
/// cached on that instance and reused by every mediator dispatch in the request, as long as
/// <see cref="HttpContext.User"/> still holds the same principal. The first <see cref="Current"/>
/// of a request allocates the 24 B context; later reads in the same request allocate nothing.
/// </para>
/// </summary>
public sealed class HttpSecurityContextAccessor(IHttpContextAccessor httpContextAccessor) : ISecurityContextAccessor
{
    private ClaimsPrincipalSecurityContext? _cached;

    public ISecurityContext Current
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user is null)
                return AnonymousSecurityContext.Instance;

            // One field read: the cached context carries the principal it was built for, so a
            // replaced HttpContext.User is never served a stale identity.
            var cached = _cached;
            if (cached is not null && ReferenceEquals(cached.Principal, user))
                return cached;

            if (user.Identity is not { IsAuthenticated: true })
                return AnonymousSecurityContext.Instance;

            cached = new ClaimsPrincipalSecurityContext(user);
            _cached = cached;
            return cached;
        }
    }
}

/// <summary>
/// Implements <see cref="ISecurityContext"/> directly on top of <see cref="ClaimsPrincipal"/>
/// without materializing the role or claim collections. <see cref="Claims"/> returns <c>this</c>,
/// and every member walks the principal's claims on demand. <see cref="Roles"/> is a separate view,
/// created the first time it is read: one object cannot be both collections, because the
/// non-generic <see cref="IEnumerable.GetEnumerator"/> it inherits from each would have to
/// enumerate both claims and roles.
/// <para>
/// The hot path is one <see cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue"/> for the
/// "scope" claim on every authorized request. It allocates nothing for a missing or single-value
/// claim, and only the joined string for a multi-value claim per RFC 6749 §3.3. That holds
/// because the walk indexes the identity and claim lists directly. The BCL helpers
/// <see cref="ClaimsPrincipal.FindFirst(string)"/> and <see cref="ClaimsPrincipal.Claims"/> hand
/// out boxed enumerators and iterator objects, about 176 B per lookup when combined. An identity
/// whose claims are not an <see cref="IList{T}"/> falls back to <c>foreach</c> and pays that
/// enumerator. Claim types are matched ordinally.
/// </para>
/// </summary>
internal sealed class ClaimsPrincipalSecurityContext(ClaimsPrincipal principal)
    : ISecurityContext, IReadOnlyDictionary<string, string>
{
    private RoleSet? _roles;

    internal ClaimsPrincipal Principal => principal;

    public string Id => principal.Identity?.Name ?? string.Empty;
    public IReadOnlySet<string> Roles => _roles ??= new RoleSet(principal);
    public IReadOnlyDictionary<string, string> Claims => this;

    // ---- IReadOnlyDictionary<string, string> ----

    public bool TryGetValue(string key, out string value)
    {
        var scan = new ClaimScan(key, joinValues: true);
        WalkClaims(principal, ref scan);
        value = scan.Joined ?? scan.First ?? string.Empty;
        return scan.First is not null;
    }

    public string this[string key] => TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key);

    public bool ContainsKey(string key)
    {
        var scan = new ClaimScan(key, joinValues: false);
        WalkClaims(principal, ref scan);
        return scan.First is not null;
    }

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

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        foreach (var key in Keys)
            if (TryGetValue(key, out var v))
                yield return new KeyValuePair<string, string>(key, v);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The role claims, as a set. Walks the principal on demand like the claims view.</summary>
    private sealed class RoleSet(ClaimsPrincipal principal) : IReadOnlySet<string>
    {
        public bool Contains(string item)
        {
            var scan = new ClaimScan(ClaimTypes.Role, joinValues: false, wantedValue: item);
            WalkClaims(principal, ref scan);
            return scan.First is not null;
        }

        public int Count
        {
            get
            {
                var n = 0;
                foreach (var _ in principal.FindAll(ClaimTypes.Role)) n++;
                return n;
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            foreach (var c in principal.FindAll(ClaimTypes.Role))
                yield return c.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsProperSubsetOf(IEnumerable<string> other) => Materialize().IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<string> other) => Materialize().IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<string> other) => Materialize().IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<string> other) => Materialize().IsSupersetOf(other);
        public bool Overlaps(IEnumerable<string> other) => Materialize().Overlaps(other);
        public bool SetEquals(IEnumerable<string> other) => Materialize().SetEquals(other);

        private HashSet<string> Materialize()
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

    // ---- allocation-free claim walk ----

    private static void WalkClaims(ClaimsPrincipal principal, ref ClaimScan scan)
    {
        var identities = principal.Identities;
        if (identities is IList<ClaimsIdentity> list)
        {
            for (var i = 0; i < list.Count && !scan.Done; i++)
                WalkIdentity(list[i], ref scan);
            return;
        }
        foreach (var identity in identities)
        {
            if (scan.Done) return;
            WalkIdentity(identity, ref scan);
        }
    }

    private static void WalkIdentity(ClaimsIdentity identity, ref ClaimScan scan)
    {
        var claims = identity.Claims;
        if (claims is IList<Claim> list)
        {
            for (var i = 0; i < list.Count && !scan.Done; i++)
                scan.Visit(list[i]);
            return;
        }
        foreach (var claim in claims)
        {
            if (scan.Done) return;
            scan.Visit(claim);
        }
    }

    private struct ClaimScan(string type, bool joinValues, string? wantedValue = null)
    {
        public string? First;
        public string? Joined;
        public bool Done;

        public void Visit(Claim claim)
        {
            if (!string.Equals(claim.Type, type, StringComparison.Ordinal)) return;
            if (wantedValue is not null && !string.Equals(claim.Value, wantedValue, StringComparison.Ordinal)) return;
            if (First is null)
            {
                First = claim.Value;
                Done = !joinValues;
                return;
            }
            Joined = string.Concat(Joined ?? First, " ", claim.Value);
        }
    }
}
