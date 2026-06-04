# HttpSecurityContextAccessor Zero-Alloc Rewrite — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Eliminate the per-authenticated-request `HashSet<string>` + `Dictionary<string,string>` allocations in `ClaimsPrincipalSecurityContext` across both ZA.Templates by implementing `IReadOnlySet<string>` and `IReadOnlyDictionary<string,string>` directly as `ClaimsPrincipal`-backed views.

**Architecture:** Both templates' `HttpSecurityContextAccessor.cs` get a mirrored rewrite. `ClaimsPrincipalSecurityContext` implements all three interfaces (`ISecurityContext` + the two view interfaces) on a single sealed class with no backing collections. Hot-path members (`TryGetValue` for claims, `Contains` for roles) walk `principal.FindAll(...)` once per call; single-value claim lookups are zero-alloc, multi-value claims allocate only the joined string (preserving RFC 6749 §3.3 space-join). BenchmarkDotNet `[MemoryDiagnoser]` measurements in each template's `MyApp.Benchmarks` project prove the 0 B/op claim on the `HasScope("scope")` hot path.

**Tech Stack:** .NET 10, C# 13, `System.Security.Claims.ClaimsPrincipal`, ZA.Authorization 2.1.0 (`ISecurityContext`), xUnit, BenchmarkDotNet `[MemoryDiagnoser]`, release-please.

**Design reference:** [docs/plans/2026-06-04-sec-context-zero-alloc-design.md](2026-06-04-sec-context-zero-alloc-design.md) (commit `ea1d24f`)
**Branch:** `feat/sec-context-zero-alloc` off `main` at `8afe430`

---

## Preflight

Before Task 1, verify SDK availability:

```powershell
dotnet --info | Select-String -Pattern "^\s+Version:" | Select-Object -First 1
```

Expected: a `10.0.x` version. If `dotnet --list-sdks` shows only e.g. `10.0.108` / `10.0.204` and `global.json` pins `10.0.300`, **relax** `global.json` temporarily:

```jsonc
// c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates/global.json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

**CRITICAL:** never commit the relaxed `global.json`. Restore it to the pinned version before Task 6's push step.

Working dir for all tasks: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.
Current branch: `feat/sec-context-zero-alloc`.

---

## Task 1: za-clean — semantic-equivalence unit tests against the OLD implementation

**Goal:** Lock in current behavior as an executable spec **before** we change the implementation. The tests pass on `8afe430`'s old code; after the rewrite they must still pass byte-for-byte.

**Files:**
- Modify: `content/za-clean/tests/MyApp.IntegrationTests/AuthorizationTests.cs`
- The system-under-test is `internal sealed class ClaimsPrincipalSecurityContext` in `content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs`. If `MyApp.Api` does NOT already grant `InternalsVisibleTo("MyApp.IntegrationTests")`, add it. Check first:
  ```powershell
  Select-String -Path content/za-clean/src/MyApp.Api/*.csproj,content/za-clean/src/MyApp.Api/Properties/AssemblyInfo.cs -Pattern InternalsVisibleTo -ErrorAction SilentlyContinue
  ```
  If nothing, add to `content/za-clean/src/MyApp.Api/MyApp.Api.csproj` under `<PropertyGroup>`:
  ```xml
  <ItemGroup>
    <InternalsVisibleTo Include="MyApp.IntegrationTests" />
  </ItemGroup>
  ```

**Step 1: Add the failing tests**

Append to `content/za-clean/tests/MyApp.IntegrationTests/AuthorizationTests.cs` (inside the existing namespace, add a new sealed class):

```csharp
using System.Security.Claims;
using MyApp.Api.Authorization;

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
```

**Step 2: Run tests against OLD code, confirm they all pass**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~ClaimsPrincipalSecurityContextTests" -v minimal
```

Expected: **6 passed, 0 failed**. This is the equivalence baseline — the OLD code must satisfy these tests so we can detect any behavior drift after the rewrite.

If any fail on the OLD code, **STOP** — investigate. Either the test expresses the wrong contract (fix the test) or the OLD code has a bug we'd inherit (separate issue).

**Step 3: Commit the equivalence-baseline tests**

```powershell
git add content/za-clean/tests/MyApp.IntegrationTests/AuthorizationTests.cs `
        content/za-clean/src/MyApp.Api/MyApp.Api.csproj
git commit -m @'
test(za-clean): equivalence-baseline tests for ClaimsPrincipalSecurityContext (#172)

Locks in current ISecurityContext semantics (RFC 6749 §3.3 space-join,
ordinal lookups, principal.Identity.Name -> Id) before the zero-alloc
rewrite. Passes against the OLD eager-materialization implementation.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 2: za-clean — rewrite ClaimsPrincipalSecurityContext as a zero-alloc view

**Goal:** Replace eager materialization with on-demand `FindAll` walks. Equivalence tests from Task 1 must keep passing byte-for-byte.

**Files:**
- Rewrite: `content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs`

**Step 1: Replace the file contents**

```csharp
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
        // Hot path: single FindAll walk. Single-value returns Claim.Value directly (0 alloc).
        // Multi-value joins per RFC 6749 §3.3 — one string.Concat only when needed.
        string? first = null;
        string? joined = null;
        foreach (var c in principal.FindAll(key))
        {
            if (first is null)
            {
                first = c.Value;
                continue;
            }
            joined = joined is null ? $"{first} {c.Value}" : $"{joined} {c.Value}";
        }
        value = joined ?? first ?? string.Empty;
        return first is not null;
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
```

**Step 2: Build**

```powershell
dotnet build content/za-clean/src/MyApp.Api/MyApp.Api.csproj -v minimal
```

Expected: **Build succeeded. 0 Warning(s). 0 Error(s).**

If errors mention missing interface members on `IReadOnlySet<string>` or `IReadOnlyDictionary<string,string>`, check the .NET 10 BCL contract — every member listed in this file should be present; missing usings (`System.Collections`) are the most likely cause.

**Step 3: Re-run the equivalence baseline tests**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~ClaimsPrincipalSecurityContextTests" -v minimal
```

Expected: **6 passed, 0 failed** — same as Task 1 Step 2. Any drift means the rewrite changed observable behavior.

**Step 4: Run the existing end-to-end authorization integration test**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~AuthorizationTests" -v minimal
```

Expected: **`POST_orders_without_required_scope_returns_403` passes** (this exercises the `[RequirePolicy("OrdersWrite")]` path through the full Kestrel/JWT/policy stack with our new view).

**Step 5: Commit**

```powershell
git add content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs
git commit -m @'
perf(za-clean): zero-alloc ClaimsPrincipalSecurityContext (#172)

Replace eager HashSet/Dictionary materialization with ClaimsPrincipal-backed
views. ISecurityContext.Roles and Claims return `this`; TryGetValue walks
FindAll(key) once. Single-value claims are zero-alloc on the hot path;
multi-value preserves RFC 6749 §3.3 space-join semantics.

Equivalence-baseline tests added in the prior commit pass unchanged,
end-to-end [RequirePolicy] integration test stays green.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 3: za-vertical-slice — mirror the equivalence baseline tests

**Goal:** Same lock-in for the vs template. The vs template currently has **no** `AuthorizationTests.cs` — auth is covered indirectly by feature-endpoint tests. We add a new dedicated file.

**Files:**
- Create: `content/za-vertical-slice/tests/MyApp.IntegrationTests/AuthorizationTests.cs`
- Possibly modify: `content/za-vertical-slice/src/MyApp/MyApp.csproj` to add `InternalsVisibleTo("MyApp.IntegrationTests")` if not already present. Check first the same way as Task 1.

**Step 1: Create the new test file**

```csharp
// content/za-vertical-slice/tests/MyApp.IntegrationTests/AuthorizationTests.cs
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
```

**Step 2: Verify it passes against the OLD vs implementation**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~ClaimsPrincipalSecurityContextTests" -v minimal
```

Expected: **6 passed, 0 failed**.

**Step 3: Commit the equivalence-baseline tests**

```powershell
git add content/za-vertical-slice/tests/MyApp.IntegrationTests/AuthorizationTests.cs `
        content/za-vertical-slice/src/MyApp/MyApp.csproj
git commit -m @'
test(za-vertical-slice): equivalence-baseline tests for ClaimsPrincipalSecurityContext (#172)

Mirrors the za-clean baseline. Passes against the OLD eager-materialization
implementation; will be re-run after the zero-alloc rewrite to confirm
semantic equivalence.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 4: za-vertical-slice — rewrite ClaimsPrincipalSecurityContext (mirror Task 2)

**Goal:** Identical rewrite to Task 2 with the vs namespace.

**Files:**
- Rewrite: `content/za-vertical-slice/src/MyApp/Authorization/HttpSecurityContextAccessor.cs`

**Step 1: Replace the file contents**

Use the **exact same body** as Task 2, with two changes:
- Namespace: `MyApp.Authorization` (not `MyApp.Api.Authorization`)
- Drop the unused `using ZeroAlloc.Mediator.Authorization;` if the vs file doesn't reference it (check the original — if it imports it, keep it; the original both templates import it, so keep).

Compare the file with za-clean's after rewriting:

```powershell
git diff --no-index `
  content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs `
  content/za-vertical-slice/src/MyApp/Authorization/HttpSecurityContextAccessor.cs
```

Expected diff: **only the namespace line differs** (`MyApp.Api.Authorization` vs `MyApp.Authorization`).

**Step 2: Build**

```powershell
dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj -v minimal
```

Expected: **Build succeeded. 0 Error(s).**

**Step 3: Re-run equivalence baseline + feature endpoint tests**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~ClaimsPrincipalSecurityContextTests" -v minimal
```

Expected: **6 passed**.

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
```

Expected: all integration tests pass (this is the vs template's full suite — Orders + Customers feature endpoint tests cover the `[RequirePolicy]` path end-to-end through our new view).

**Step 4: Commit**

```powershell
git add content/za-vertical-slice/src/MyApp/Authorization/HttpSecurityContextAccessor.cs
git commit -m @'
perf(za-vertical-slice): zero-alloc ClaimsPrincipalSecurityContext (#172)

Mirror of the za-clean rewrite. Replaces eager HashSet/Dictionary
materialization with ClaimsPrincipal-backed views; single-value
TryGetValue is zero-alloc on the hot path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: za-clean — SecurityContextBench (allocation proof)

**Goal:** Empirically prove **0 B/op** on the hot path with BenchmarkDotNet `[MemoryDiagnoser]`.

**Files:**
- Create: `content/za-clean/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs`

**Step 1: Create the benchmark**

```csharp
// content/za-clean/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs
using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using MyApp.Api.Authorization;
using MyApp.Application.Authorization;
using ZeroAlloc.Authorization;

namespace MyApp.Benchmarks;

/// <summary>
/// Allocation benchmark for ClaimsPrincipalSecurityContext + OrdersReadPolicy.HasScope.
/// Asserts the post-#172 rewrite is zero-alloc on the single-value hot path and only
/// allocates one joined string in the multi-value-claim case.
/// </summary>
[MemoryDiagnoser]
public class SecurityContextBench
{
    private ISecurityContext _singleScopeCtx = null!;
    private ISecurityContext _multiScopeCtx = null!;
    private ISecurityContext _noScopeCtx = null!;

    [GlobalSetup]
    public void Setup()
    {
        _singleScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("scope", "orders.read orders.write"),
        }, "test")));

        _multiScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("scope", "orders.read"),
            new Claim("scope", "orders.write"),
        }, "test")));

        _noScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
        }, "test")));
    }

    /// <summary>Single-value scope claim (RFC 6749 §3.3 space-separated). Target: 0 B/op.</summary>
    [Benchmark]
    public bool HasScope_SingleValue() => OrdersReadPolicy.HasScope(_singleScopeCtx, "orders.read");

    /// <summary>Multi-value (two separate "scope" claims). Allocates exactly one joined string.</summary>
    [Benchmark]
    public bool HasScope_MultiValueClaims() => OrdersReadPolicy.HasScope(_multiScopeCtx, "orders.read");

    /// <summary>Missing claim — early-out path. Target: 0 B/op.</summary>
    [Benchmark]
    public bool HasScope_Missing() => OrdersReadPolicy.HasScope(_noScopeCtx, "orders.read");
}
```

**Note on visibility:** `OrdersReadPolicy.HasScope` is `internal static`. The benchmark project already references `MyApp.Api.csproj` (which transitively references `MyApp.Application.csproj`). Add `InternalsVisibleTo("MyApp.Benchmarks")` to `MyApp.Application.csproj` if needed:

```powershell
Select-String -Path content/za-clean/src/MyApp.Application/*.csproj -Pattern "InternalsVisibleTo"
```

If missing, add to `MyApp.Application.csproj`:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="MyApp.Benchmarks" />
  <InternalsVisibleTo Include="MyApp.IntegrationTests" />
  <InternalsVisibleTo Include="MyApp.UnitTests" />
</ItemGroup>
```
Preserve any existing entries.

**Step 2: Build the benchmark**

```powershell
dotnet build content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj -c Release -v minimal
```

Expected: **Build succeeded.**

**Step 3: Run the benchmark and capture results**

```powershell
dotnet run -c Release --project content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj `
  -- --filter "*SecurityContextBench*" --memory
```

Expected results table (column "Allocated"):
- `HasScope_SingleValue` — **0 B**
- `HasScope_MultiValueClaims` — small (≤ 64 B, the joined string + maybe a Claim enumerator state machine)
- `HasScope_Missing` — **0 B**

If `HasScope_SingleValue` shows non-zero allocations:
- Most likely cause: the `principal.FindAll(key)` iterator's enumerator allocates on `foreach`. In modern BCL it returns a value-type enumerator wrapped in `IEnumerable<Claim>` — but `foreach` on an interface boxes. Inspect the disassembly or rewrite the hot-path member to call `principal.FindFirst(key)` for the common single-value case, falling back to `FindAll` only when a second match might exist. Acceptable rewrite:
  ```csharp
  public bool TryGetValue(string key, out string value)
  {
      var first = principal.FindFirst(key);
      if (first is null) { value = string.Empty; return false; }
      // Probe for a second one; if there's only one, no enumerator alloc.
      string? joined = null;
      var seenFirst = false;
      foreach (var c in principal.FindAll(key))
      {
          if (!seenFirst) { seenFirst = true; continue; }
          joined = joined is null ? $"{first.Value} {c.Value}" : $"{joined} {c.Value}";
      }
      value = joined ?? first.Value;
      return true;
  }
  ```
  If even this allocs, accept the small enumerator state-machine alloc — document the measured number in the design doc and the commit message; the goal is "no per-request collection allocation", which this achieves.

- If allocations remain higher than ~64 B, **STOP** and report.

**Step 4: Save the benchmark output**

```powershell
mkdir -Force docs/benchmarks 2>$null
Copy-Item content/za-clean/benchmarks/MyApp.Benchmarks/BenchmarkDotNet.Artifacts/results/MyApp.Benchmarks.SecurityContextBench-report-github.md `
  docs/benchmarks/2026-06-04-za-clean-sec-context-alloc.md
```

**Step 5: Commit**

```powershell
git add content/za-clean/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs `
        content/za-clean/src/MyApp.Application/MyApp.Application.csproj `
        docs/benchmarks/2026-06-04-za-clean-sec-context-alloc.md
git commit -m @'
bench(za-clean): SecurityContextBench proves 0 B/op on HasScope hot path (#172)

BenchmarkDotNet [MemoryDiagnoser] measurements show:
  HasScope_SingleValue      - 0 B/op
  HasScope_MultiValueClaims - small (one joined string)
  HasScope_Missing          - 0 B/op

Saved as docs/benchmarks/2026-06-04-za-clean-sec-context-alloc.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 6: za-vertical-slice — SecurityContextBench (mirror Task 5)

**Goal:** Same benchmark for the vs template.

**Files:**
- Create: `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs`

**Step 1: Create the benchmark**

Same code as Task 5 Step 1, with three changes:
- `using MyApp.Api.Authorization;` → `using MyApp.Authorization;`
- `using MyApp.Application.Authorization;` → (drop — vs collapses Application into MyApp; `OrdersReadPolicy` is in `MyApp.Authorization`)
- The `InternalsVisibleTo` for `MyApp.Benchmarks` goes on `MyApp.csproj` instead of `MyApp.Application.csproj`.

**Step 2 / Step 3 / Step 4:** identical commands to Task 5 with the vs paths:

```powershell
dotnet build content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj -c Release -v minimal

dotnet run -c Release --project content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj `
  -- --filter "*SecurityContextBench*" --memory

Copy-Item content/za-vertical-slice/benchmarks/MyApp.Benchmarks/BenchmarkDotNet.Artifacts/results/MyApp.Benchmarks.SecurityContextBench-report-github.md `
  docs/benchmarks/2026-06-04-za-vertical-slice-sec-context-alloc.md
```

Expected: identical pattern to Task 5 (0 B / small / 0 B).

**Step 5: Commit**

```powershell
git add content/za-vertical-slice/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs `
        content/za-vertical-slice/src/MyApp/MyApp.csproj `
        docs/benchmarks/2026-06-04-za-vertical-slice-sec-context-alloc.md
git commit -m @'
bench(za-vertical-slice): SecurityContextBench proves 0 B/op on HasScope hot path (#172)

Mirror of za-clean SecurityContextBench. Same allocation profile.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 7: Full-suite sweep — both templates

**Goal:** Confirm nothing regresses across the full integration suite of either template before pushing.

**Step 1: za-clean full sweep**

```powershell
dotnet test content/za-clean/ZeroAlloc.Templates.Clean.sln -v minimal
```

If no .sln in that path, fall back to per-project:

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -v minimal
```

Expected: all green. Note that `MyApp.ArchitectureTests` uses NetArchTest — the new view types don't cross any boundary the existing rules forbid (they live inside `MyApp.Api.Authorization`, same as before).

**Step 2: za-vertical-slice full sweep**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj -v minimal
```

Expected: all green.

**Step 3: Restore `global.json` if it was relaxed in Preflight**

```powershell
git status global.json
```

If modified, restore:
```powershell
git checkout -- global.json
```

**Step 4: No commit — this is a verification gate.** If anything fails, stop and report.

---

## Task 8: Push + PR + admin-merge + release-please

**Goal:** Land the branch; release-please opens a `chore(main): release 0.14.0` PR.

**Step 1: Push the branch**

```powershell
git push -u origin feat/sec-context-zero-alloc
```

**Step 2: Open the PR**

```powershell
gh pr create --title "perf(authorization): zero-alloc ClaimsPrincipalSecurityContext across both templates" --body @'
## Summary

Closes #172. Removes the per-authenticated-request `HashSet<string>` (roles) and `Dictionary<string,string>` (claims) allocations in `ClaimsPrincipalSecurityContext` across both templates by implementing `IReadOnlySet<string>` and `IReadOnlyDictionary<string,string>` directly as `ClaimsPrincipal`-backed views — no backing collections, no eager materialization.

- **Hot path** (`OrdersReadPolicy.HasScope` -> `ctx.Claims.TryGetValue("scope")`): **0 B/op** for single-value claims, one joined string only for multi-value (RFC 6749 §3.3 preserved).
- **`ctx.Roles`** (never accessed by either template today) is also a view — dormant by default.
- Both templates ship the identical rewrite.

## Approach

Approach A from the brainstorm (template-side fix, no upstream ZA.Authorization change). Design: `docs/plans/2026-06-04-sec-context-zero-alloc-design.md`.

## Evidence

BenchmarkDotNet `[MemoryDiagnoser]` measurements:
- `docs/benchmarks/2026-06-04-za-clean-sec-context-alloc.md`
- `docs/benchmarks/2026-06-04-za-vertical-slice-sec-context-alloc.md`

## Test plan

- [x] Equivalence-baseline unit tests added in both templates, pass against OLD implementation, pass unchanged after rewrite.
- [x] za-clean `AuthorizationTests.POST_orders_without_required_scope_returns_403` stays green (end-to-end [RequirePolicy] through the new view).
- [x] za-vertical-slice feature-endpoint integration tests (Orders + Customers) stay green.
- [x] za-clean ArchitectureTests stay green (no new boundary crossings).
- [x] za-vertical-slice ConventionTests stay green.
- [x] `SecurityContextBench` allocation results captured in `docs/benchmarks/`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

**Step 3: Wait for CI**

```powershell
gh pr checks --watch
```

Expected: all required checks pass.

**Step 4: Admin-merge with `perf:` squash title**

```powershell
$prNumber = gh pr view --json number -q .number
gh pr merge $prNumber --squash --admin --subject "perf(authorization): zero-alloc ClaimsPrincipalSecurityContext across both templates (#$prNumber)"
```

The `perf:` prefix matches release-please's `release-please-config.json` mapping for minor bump.

**Step 5: Monitor release-please**

```powershell
# Poll for ~60 seconds — the release-please workflow runs on push to main.
for ($i = 0; $i -lt 6; $i++) {
    $rp = gh pr list --label autorelease:pending --json number,title -q '.[0]'
    if ($rp) { Write-Host "Release-please PR: $rp"; break }
    Start-Sleep -Seconds 10
}
```

Expected: a `chore(main): release 0.14.0` PR opens (likely #174 or thereabouts).

**Step 6: Report — DO NOT MERGE release-please's PR**

That's the user's call. They'll also run `gh workflow run pack-push.yml -f version=0.14.0` manually after merging it. Final report should include:
- Squash-merge SHA of the perf PR
- Release-please PR number
- Allocation benchmark numbers (paste the two `Allocated` columns from the bench output)

---

## Notes on TDD discipline

- **Tasks 1 and 3** are pure equivalence-baseline lock-ins against the OLD code. They MUST pass before any rewrite.
- **Tasks 2 and 4** must re-run those baselines after the rewrite. Any drift = behavior changed; stop and investigate.
- **Tasks 5 and 6** add the allocation measurement. If the 0 B/op target isn't hit, the fallback in Task 5 Step 3 covers the `FindAll` enumerator boxing case.
- Frequent commits (one per task), `perf:` / `test:` / `bench:` prefixes per commit so the squash-merge title can fairly summarize the change as `perf(authorization):`.
