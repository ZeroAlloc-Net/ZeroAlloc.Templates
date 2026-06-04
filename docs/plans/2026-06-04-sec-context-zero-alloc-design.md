# HttpSecurityContextAccessor Zero-Alloc Rewrite — Design

**Status:** approved 2026-06-04
**Scope:** ZA.Templates `za-clean` + `za-vertical-slice` — template-side fix, no upstream change
**Target version:** ZA.Templates v0.14.0
**Closes:** [#172](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/172)
**Branch:** `feat/sec-context-zero-alloc` off `main` at `8afe430`

## Background

`HttpSecurityContextAccessor` bridges `HttpContext.User` (`ClaimsPrincipal`) into ZA.Authorization's `ISecurityContext`. The current implementation lazily materializes two collections on first access:

- `HashSet<string>` of role claim values (`MaterializeRoles`)
- `Dictionary<string,string>` of all claim type→value pairs, with multi-value claims space-joined per RFC 6749 §3.3 (`MaterializeClaims`)

These collections are allocated per authenticated request once `Roles` or `Claims` is touched. At a few thousand RPS that's thousands of short-lived collections/sec on the hot path — visible GC pressure on a template whose tagline is "zero-alloc through the framework hot path."

Actual usage in both templates (grep-verified):
- `ctx.Roles` is **never accessed** anywhere — the `HashSet` is pure waste today.
- `ctx.Claims` is accessed in exactly one place: `OrdersPolicies.HasScope` calls `ctx.Claims.TryGetValue("scope", out var scopes)` once per `[RequirePolicy]` invocation.

We allocate a full `Dictionary<string,string>` to satisfy one `TryGetValue` against one key.

## Decision

Adopt **Approach A** from the brainstorm: implement `IReadOnlySet<string>` and `IReadOnlyDictionary<string,string>` directly on `ClaimsPrincipalSecurityContext`, with members that walk `ClaimsPrincipal.FindAll` on demand. No `HashSet`/`Dictionary` is allocated; the per-request collection churn is gone.

Rejected:
- **Approach B** (drop `Roles` only): doesn't close the headline allocation site.
- **Approach C** (add `TryGetClaim` upstream to ZA.Authorization, mark `Claims` `[Obsolete]`): premature upstream change. The interface is a standard BCL shape and isn't actually the problem — eager materialization is. Approach A solves it template-side without coordinating an upstream minor release.

## What changes

**Files modified (2 source + 2 test + 2 bench, mirrored across templates):**

1. **`content/za-clean/src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs`** — rewrite `ClaimsPrincipalSecurityContext` to implement both view interfaces directly. No nested adapter types; one sealed class implements all three of `ISecurityContext`, `IReadOnlySet<string>`, and `IReadOnlyDictionary<string,string>`.

   Hot-path members (`TryGetValue` for claims, `Contains` for roles) walk `principal.FindAll(...)` once. Single-value claims return the claim's `Value` directly with zero allocation. Multi-value claims allocate one joined `string` only when `FindAll` yields more than one entry, preserving the existing RFC 6749 §3.3 space-join semantics.

   Cold-path members (`Count`, `Keys`, `Values`, `GetEnumerator`, `ContainsKey`, `this[key]`, set operations like `IsSubsetOf`) are implemented for correctness; none are called in either template today, so their per-call walks don't matter.

2. **`content/za-vertical-slice/src/MyApp/Authorization/HttpSecurityContextAccessor.cs`** — same rewrite, character-identical except for the namespace (`MyApp.Authorization` vs `MyApp.Api.Authorization`).

3. **`content/za-clean/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs`** *(new)* — BenchmarkDotNet `[MemoryDiagnoser]` benchmark:
   - `HasScope_SingleValue` — happy path: claims principal has one `scope` claim with value `"orders.read orders.write"`. Expect **0 B/op**.
   - `HasScope_MultiValueClaims` — principal has two `scope` claims. Expect **one** joined-string alloc (multi-value semantics).
   - `HasScope_Missing` — principal has no `scope` claim. Expect **0 B/op**.

4. **`content/za-vertical-slice/benchmarks/MyApp.Benchmarks/SecurityContextBench.cs`** *(new)* — same.

5. **`content/za-clean/tests/MyApp.IntegrationTests/AuthorizationTests.cs`** — add unit tests at the `ClaimsPrincipalSecurityContext` level:
   - `Claims_TryGetValue_returns_single_value_unchanged`
   - `Claims_TryGetValue_joins_multi_value_with_space` (RFC 6749 §3.3)
   - `Claims_TryGetValue_missing_returns_false`
   - `Roles_Contains_hits_existing_role`
   - `Roles_Contains_misses_unknown_role`

   The existing end-to-end `[RequirePolicy]` integration tests stay unchanged — behavior is preserved.

6. **`content/za-vertical-slice/tests/MyApp.IntegrationTests/AuthorizationTests.cs`** — same unit tests.

## Versioning + release

- `perf(za-clean,za-vertical-slice):` or `refactor(authorization):` commit type — additive performance improvement, no API change.
- release-please cuts **v0.14.0** (minor — performance improvement on a user-visible code path; users will diff the template file).
- Squash title at merge: `perf:` so release-please fires.

## Tests + acceptance

- All existing tests stay green in both templates.
- New unit tests cover the view semantics (single, multi, missing).
- New BenchmarkDotNet measurements demonstrate:
  - `HasScope_SingleValue` and `HasScope_Missing` allocate **0 B/op**.
  - `HasScope_MultiValueClaims` allocates exactly one joined string (~32–48 B depending on values).
- Existing `AuthorizationTests` integration tests pass — `[RequirePolicy]` end-to-end behavior unchanged.

## What stays out of scope

- **Pooling `ClaimsPrincipalSecurityContext` across requests.** The accessor still creates one per authenticated request via `new ClaimsPrincipalSecurityContext(user)`. Small (~24 B), separate from #172. Follow-up if a profile shows it dominates.
- **Upstream ZA.Authorization API change.** Approach C — only if Approach A's per-call walks ever appear in a profile. They won't until a policy enumerates the full claim set.
- **Issues #170 / #171 / #173.** Separate read-path concerns.

## Risk

- **Interface contract completeness.** `IReadOnlyDictionary<string,string>` has ~7 members; missing one breaks build. We implement all of them — cold-path correctness over hot-path perf.
- **Multi-value semantics drift.** The existing code uses string interpolation `$"{existing} {c.Value}"` inside the materialization loop, which builds the joined string as it goes. The new code walks `FindAll(key)` and joins all values for that key only. Result is identical for the "scope" use case. Cross-checked by the multi-value unit test.
- **AOT compatibility.** No reflection, no expression trees, no LINQ. `ClaimsPrincipal.FindAll(string)` is AOT-clean. ✔.
- **Per-call walk cost** if a future policy calls `TryGetValue` for many keys in a tight loop. Acceptable — single `TryGetValue("scope")` per request today, and the walk is `O(claims)` (typically 5–10 claims on a JWT).
