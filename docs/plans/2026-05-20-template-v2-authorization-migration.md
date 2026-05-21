# `za-clean` Template — v2 Authorization Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate the `za-clean` template in-place to consume `ZeroAlloc.Authorization 2.0.0` + `ZeroAlloc.Mediator.Authorization 2.0.0`, so new apps scaffolded from `dotnet new za-clean` get the v2 shape (`[Policy]` / `[RequirePolicy]` attributes, async-only `IAuthorizationPolicy`, two-call DI setup).

**Architecture:** Mechanical rewrite of 5 source files + central package pins + Program.cs + AGENTS.md. The CI workflows (`build`, `real-run-smoke`, `aot-publish-smoke`) need no changes — they pick up v2 packages transparently once `Directory.Packages.props` updates land. Coordination dependency: `ZeroAlloc.Mediator.Authorization 2.0.0` must be on NuGet before opening the PR (Task 0 verifies).

**Tech Stack:** C# / .NET 10 (`net10.0`), Microsoft.NET.Sdk (web), `ZeroAlloc.Authorization 2.0.0`, `ZeroAlloc.Mediator.Authorization 2.0.0`, `ZeroAlloc.Mediator 4.1.4`, Central Package Management, release-please for patch-bump via `chore(deps):` conventional commits.

**Reference design:** [2026-05-20-template-v2-authorization-migration-design.md](2026-05-20-template-v2-authorization-migration-design.md)

**Repo root for all paths below:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates/`

**Branch:** `feat/template-v2-authorization-migration` (already created and on HEAD `593e6ce` — the committed design doc).

---

## Pre-flight notes for the executor

- **OS:** Windows. Use PowerShell. **Bash is NOT available.**
- **SDK:** local `10.0.204` is fine; `global.json` is not blocking here.
- **Conventional commits:** every commit subject MUST be `chore(deps): ...` (drives release-please to patch-bump, NOT minor/major). Do NOT use `feat:` or `feat!:` — that would push a wrong version bump.
- **Repo gotchas (inherited from ZA.Authorization v2 + Mediator.Authorization v2 execution):**
  - **`UnitResult<T>.Success()`** (generic-class-static-method form), NOT `UnitResult.Success<T>()`. Failures via bare `new AuthorizationFailure(code, reason)` — implicit conversion to `UnitResult<AuthorizationFailure>`.
  - **ZA0601 analyzer** bans LINQ inside loops in src code. Manual `foreach`/`for`. (Template content uses minimal LINQ — unlikely to bite.)
  - **TreatWarningsAsErrors** is enabled in the template.
- **TDD discipline:** the template doesn't have unit tests against the source we're changing. The "test" is `dotnet new` + `dotnet build` + `dotnet test` against a scaffolded app. We exercise this in Task 7.
- **One commit per task.**
- **NO PUSH** until all tasks complete AND `ZeroAlloc.Mediator.Authorization 2.0.0` is on NuGet. Pushing before that just makes CI noise.

---

## Tasks at a glance

| # | Task | Files | Commit message |
|---|---|---|---|
| 0 | Pre-flight: verify SDK + NuGet availability + branch state | — | — |
| 1 | Bump Directory.Packages.props (3 pin changes) | `content/za-clean/Directory.Packages.props` | `chore(deps): bump ZeroAlloc.Authorization to 2.0.0 + ZeroAlloc.Mediator.Authorization to 2.0.0` |
| 2 | Drop Generator PackageReference from MyApp.Application.csproj | `content/za-clean/src/MyApp.Application/MyApp.Application.csproj` | `chore(deps): drop ZeroAlloc.Mediator.Authorization.Generator (bundled into ZA.Authorization v2)` |
| 3 | Rewrite OrdersPolicies.cs for v2 (async-only EvaluateAsync, `[Policy]` attr) | `content/za-clean/src/MyApp.Application/Authorization/OrdersPolicies.cs` | `chore(deps): migrate OrdersPolicies to v2 IAuthorizationPolicy + [Policy]` |
| 4 | Rename `[Authorize]` → `[RequirePolicy]` in command + query | `content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderCommand.cs`, `content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdQuery.cs` | `chore(deps): rename [Authorize] to [RequirePolicy] for v2 attribute name` |
| 5 | Add `services.AddZeroAllocAuthorization()` to Program.cs (D3 guard requirement) | `content/za-clean/src/MyApp.Api/Program.cs` | `chore(deps): register AddZeroAllocAuthorization for v2 D3 guard` |
| 6 | Local end-to-end verification: scaffold + build + test + AOT smoke | — | — |
| 7 | Add proactive AOT gotcha row to AGENTS.md | `content/za-clean/AGENTS.md` | `chore(docs): add proactive AOT gotcha row for IAuthorizedRequest<TPayload>` |
| 8 | Push + open PR (only after `ZeroAlloc.Mediator.Authorization 2.0.0` is on NuGet) | — | — |

---

### Task 0: Pre-flight

**Step 1: Confirm branch + clean tree.**

```powershell
Set-Location c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
git branch --show-current
git status --short
```

Expected: branch `feat/template-v2-authorization-migration`, clean tree.

**Step 2: Confirm `ZeroAlloc.Mediator.Authorization 2.0.0` is on NuGet.**

```powershell
$resp = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/zeroalloc.mediator.authorization/index.json" -ErrorAction SilentlyContinue
$resp.versions | Select-Object -Last 8
```

Expected: includes `2.0.0`. **If 2.0.0 is NOT listed, STOP** — release-please PR #88 on ZA.Mediator hasn't merged yet, or the publish workflow is still running. We can do Tasks 1-5 locally (they don't restore), then resume Task 6 (which does restore) once 2.0.0 publishes.

Confirm `ZeroAlloc.Authorization 2.0.0` is also there (should be — it was shipped earlier):
```powershell
$resp = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/zeroalloc.authorization/index.json"
$resp.versions | Select-Object -Last 5
```

Expected: includes `2.0.0`.

**Step 3: Confirm baseline.**

```powershell
dotnet --version
```

Expected: `10.0.204` (or higher).

No commit for Task 0.

---

### Task 1: Bump Directory.Packages.props (3 pin changes)

**Files:**
- Modify: `content/za-clean/Directory.Packages.props`

**Step 1: Read the current state.**

Line 14: `<PackageVersion Include="ZeroAlloc.Authorization" Version="1.2.2" />`
Line 21: `<PackageVersion Include="ZeroAlloc.Mediator.Authorization" Version="4.1.4" />`
Line 22: `<PackageVersion Include="ZeroAlloc.Mediator.Authorization.Generator" Version="4.1.3" />`

**Step 2: Apply three edits:**

1. Line 14 — bump `ZeroAlloc.Authorization` from `1.2.2` to `2.0.0`:
   ```xml
   <PackageVersion Include="ZeroAlloc.Authorization" Version="2.0.0" />
   ```

2. Line 21 — bump `ZeroAlloc.Mediator.Authorization` from `4.1.4` to `2.0.0` (split-versioning major decrement is intentional — v2.0.0 is the new versioning trajectory after the split):
   ```xml
   <PackageVersion Include="ZeroAlloc.Mediator.Authorization" Version="2.0.0" />
   ```

3. Line 22 — **delete the line entirely** (`ZeroAlloc.Mediator.Authorization.Generator` is bundled into `ZeroAlloc.Authorization 2.0.0`'s analyzer payload; no separate package exists):
   ```xml
   <PackageVersion Include="ZeroAlloc.Mediator.Authorization.Generator" Version="4.1.3" />
   ```

**Step 3: Sanity check.**

```powershell
Select-String -Path content/za-clean/Directory.Packages.props -Pattern "ZeroAlloc.Authorization|ZeroAlloc.Mediator.Authorization"
```

Expected output: exactly 2 lines, both at `2.0.0`. The `.Generator` line MUST be gone.

**Step 4: Commit.**

```powershell
git add content/za-clean/Directory.Packages.props
git commit -m "chore(deps): bump ZeroAlloc.Authorization to 2.0.0 + ZeroAlloc.Mediator.Authorization to 2.0.0"
```

---

### Task 2: Drop Generator PackageReference from MyApp.Application.csproj

**Files:**
- Modify: `content/za-clean/src/MyApp.Application/MyApp.Application.csproj`

**Step 1: Find the block to delete.**

Lines 19-22:
```xml
    <PackageReference Include="ZeroAlloc.Mediator.Authorization.Generator">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
```

**Step 2: Delete those 4 lines entirely.**

After the edit, the surrounding context should look like:
```xml
    <PackageReference Include="ZeroAlloc.Authorization" />
    <PackageReference Include="ZeroAlloc.Mediator.Authorization" />
    <PackageReference Include="ZeroAlloc.Mapping">
```

(The `ZeroAlloc.Mediator.Authorization.Generator` block sat between `ZeroAlloc.Mediator.Authorization` and `ZeroAlloc.Mapping` — confirm via current file read.)

**Step 3: Sanity check.**

```powershell
Select-String -Path content/za-clean/src/MyApp.Application/MyApp.Application.csproj -Pattern "ZeroAlloc.Mediator.Authorization"
```

Expected: exactly **1 match** (`<PackageReference Include="ZeroAlloc.Mediator.Authorization" />`), not 2. The Generator reference is gone.

**Step 4: Commit.**

```powershell
git add content/za-clean/src/MyApp.Application/MyApp.Application.csproj
git commit -m "chore(deps): drop ZeroAlloc.Mediator.Authorization.Generator (bundled into ZA.Authorization v2)"
```

---

### Task 3: Rewrite OrdersPolicies.cs for v2

**Files:**
- Modify: `content/za-clean/src/MyApp.Application/Authorization/OrdersPolicies.cs`

**Step 1: Read the current file** (50 lines). Both policy classes implement v1 `bool IsAuthorized(ISecurityContext)`. The `HasScope` helper (allocation-free scope-claim scanning) is unchanged in v2.

**Step 2: Rewrite the entire file** to:

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

#pragma warning disable MA0048 // two policies intentionally co-located in one file

namespace MyApp.Application.Authorization;

/// <summary>
/// Grants access to read-side order operations (queries). Requires the
/// "orders.read" scope claim, mirroring the endpoint-level "OrdersRead"
/// ASP.NET policy in Program.cs.
/// </summary>
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
[Policy("OrdersWrite")]
public sealed class OrdersWritePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(OrdersReadPolicy.HasScope(ctx, "orders.write")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.write scope"));
}
```

Key v2 shape elements:
- `[AuthorizationPolicy("...")]` → `[Policy("...")]`
- `bool IsAuthorized(ISecurityContext)` → `ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext, CancellationToken)`
- Sync-completing wrap in `new ValueTask<...>(syncResult)` — allocation-free
- Failure via `new AuthorizationFailure(code, reason)` — implicit conversion to `UnitResult<AuthorizationFailure>`
- `using ZeroAlloc.Results;` added (for `UnitResult<T>.Success()`)
- `HasScope` helper unchanged

**Step 3: Sanity check.**

```powershell
Select-String -Path content/za-clean/src/MyApp.Application/Authorization/OrdersPolicies.cs -Pattern "AuthorizationPolicy|IsAuthorized"
```

Expected: **0 matches** (both v1 names should be gone).

```powershell
Select-String -Path content/za-clean/src/MyApp.Application/Authorization/OrdersPolicies.cs -Pattern "Policy\(|EvaluateAsync"
```

Expected: 2 `[Policy(` matches + 2 `EvaluateAsync` overrides.

**Step 4: Commit.**

```powershell
git add content/za-clean/src/MyApp.Application/Authorization/OrdersPolicies.cs
git commit -m "chore(deps): migrate OrdersPolicies to v2 IAuthorizationPolicy + [Policy]"
```

---

### Task 4: Rename `[Authorize]` → `[RequirePolicy]` in command + query

**Files:**
- Modify: `content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderCommand.cs`
- Modify: `content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdQuery.cs`

**Step 1: Read both files.**

Each has one occurrence of `[Authorize("...")]`:
- `CreateOrderCommand.cs` line 13: `[Authorize("OrdersWrite")]`
- `GetOrderByIdQuery.cs` line 8: `[Authorize("OrdersRead")]`

**Step 2: Apply the renames.**

In `CreateOrderCommand.cs`, change:
```csharp
[Authorize("OrdersWrite")]
```
to:
```csharp
[RequirePolicy("OrdersWrite")]
```

In `GetOrderByIdQuery.cs`, change:
```csharp
[Authorize("OrdersRead")]
```
to:
```csharp
[RequirePolicy("OrdersRead")]
```

No `using` changes needed — both attributes live in `ZeroAlloc.Authorization` namespace which is presumably already imported (or pulled in via `ImplicitUsings`). If a compile error fires later, add `using ZeroAlloc.Authorization;` to each file.

**Step 3: Sanity check.**

```powershell
Select-String -Path content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderCommand.cs,content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdQuery.cs -Pattern "Authorize\(|RequirePolicy\("
```

Expected: 2 `[RequirePolicy(...)]` matches, 0 `[Authorize(...)]` matches.

**Step 4: Commit.**

```powershell
git add content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderCommand.cs content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdQuery.cs
git commit -m "chore(deps): rename [Authorize] to [RequirePolicy] for v2 attribute name"
```

---

### Task 5: Add `services.AddZeroAllocAuthorization()` to Program.cs

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/Program.cs`

**Step 1: Locate the insertion point.**

Around line 47-48 (in current file):
```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddMyAppApplication(opt => opt.UseAccessor<HttpSecurityContextAccessor>());
```

`AddMyAppApplication` internally calls `WithAuthorization(...)` which in v2 includes the D3 guard — it throws `InvalidOperationException` if `AddZeroAllocAuthorization()` wasn't called first.

**Step 2: Insert one new line BEFORE `AddMyAppApplication`.**

The block becomes:
```csharp
builder.Services.AddHttpContextAccessor();
// v2: register the source-generated policy registry (AuthorizerFor<T> dispatchers +
// [Policy] classes as scoped) before WithAuthorization() runs. The D3 guard inside
// AddMyAppApplication's WithAuthorization() throws InvalidOperationException if this
// call is missing or runs after AddMediator(). See ZeroAlloc.Authorization v2 docs.
builder.Services.AddZeroAllocAuthorization();
builder.Services.AddMyAppApplication(opt => opt.UseAccessor<HttpSecurityContextAccessor>());
```

**Step 3: Check `using` directives at top of Program.cs.**

Read the top of `Program.cs` to see what's imported. Add this near the top with the other ZA usings:

```csharp
using ZeroAlloc.Authorization.Generated;
```

(This namespace is where the source generator emits `AddZeroAllocAuthorization`. It's NOT pulled in transitively because the generator emits into a specific namespace.)

If the top of Program.cs already has `using ZeroAlloc.Authorization;`, the new line goes immediately after it.

**Step 4: Sanity check.**

```powershell
Select-String -Path content/za-clean/src/MyApp.Api/Program.cs -Pattern "AddZeroAllocAuthorization|using ZeroAlloc.Authorization.Generated"
```

Expected: 1 `using ZeroAlloc.Authorization.Generated;` line + 1 `services.AddZeroAllocAuthorization()` call.

**Step 5: Commit.**

```powershell
git add content/za-clean/src/MyApp.Api/Program.cs
git commit -m "chore(deps): register AddZeroAllocAuthorization for v2 D3 guard"
```

---

### Task 6: Local end-to-end verification

This is verification, not implementation — no commit. We scaffold a fresh app from the local template and run its full test suite + AOT smoke to validate the v2 wiring works.

**Step 1: Locate the template's pack output.**

Look at the existing CI workflow (`.github/workflows/ci.yml`) to find how the template gets packed locally. Typical pattern:

```powershell
dotnet pack templates/ZeroAlloc.Templates.csproj -c Release -o artifacts/local
```

(Verify the exact csproj path via `Get-ChildItem -Recurse -Filter "*.Templates.csproj"` from repo root.)

**Step 2: Install the locally-packed template.**

```powershell
$pkg = Get-ChildItem artifacts/local/ZeroAlloc.Templates.*.nupkg | Select-Object -First 1
dotnet new uninstall ZeroAlloc.Templates 2>$null  # safe if not installed
dotnet new install $pkg.FullName
```

Expected: template `za-clean` listed in `dotnet new list`.

**Step 3: Scaffold a fresh app in a temp dir.**

```powershell
$scaffold = Join-Path $env:TEMP "za-clean-v2-scaffold-$(New-Guid)"
New-Item -ItemType Directory -Path $scaffold | Out-Null
Set-Location $scaffold
dotnet new za-clean -n MyApp -o .
```

**Step 4: Build the scaffolded app.**

```powershell
dotnet build -c Release
```

Expected: 0 errors. Capture any warnings — `ZAUTH001` (unknown policy name) or `ZAUTH002` (duplicate policy) would indicate the migration missed a rename somewhere.

**Step 5: Run the scaffolded app's tests.**

```powershell
dotnet test -c Release
```

Expected: all tests pass. The `AuthorizationTests` in MyApp.IntegrationTests are particularly relevant — they exercise the auth path end-to-end via TestServer.

**Step 6: (Optional but recommended) AOT publish smoke.**

```powershell
dotnet publish src/MyApp.Api/MyApp.Api.csproj -c Release -r win-x64 -p:PublishAot=true
$exe = Get-ChildItem src/MyApp.Api/bin/Release/net10.0/win-x64/publish/MyApp.Api.exe | Select-Object -First 1
& $exe.FullName --urls "http://localhost:6789" &
Start-Sleep -Seconds 3
$resp = Invoke-WebRequest -Uri "http://localhost:6789/healthz" -UseBasicParsing -ErrorAction SilentlyContinue
Get-Process MyApp.Api | Stop-Process -Force -ErrorAction SilentlyContinue
$resp.StatusCode
```

Expected: `200`. If AOT publish fails on Windows due to missing MSVC linker, that's a host environment issue; the linux-x64 CI gate (`aot-publish-smoke`) will validate.

**Step 7: Cleanup.**

```powershell
Set-Location c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
Remove-Item -Recurse -Force $scaffold
dotnet new uninstall ZeroAlloc.Templates
```

**Step 8: STOP and report** if any step fails. If all green, proceed to Task 7.

No commit for Task 6.

---

### Task 7: Add proactive AOT gotcha row to AGENTS.md

**Files:**
- Modify: `content/za-clean/AGENTS.md`

**Step 1: Read the existing "ZA-specific gotchas" section** (around lines 88-101 per recon).

**Step 2: Sweep for any v1 attribute name references** in surrounding prose (`[AuthorizationPolicy]`, `[Authorize]`). Rewrite to v2 (`[Policy]`, `[RequirePolicy]`). Per recon, this might be a 1-2 line touch.

**Step 3: Append a new row to the gotchas table.**

Insert this new row inside the existing table (after the last existing row, before any `</table>` or section break):

```markdown
| Switching a request to `IAuthorizedRequest<TPayload>` for Result-style auth | Under AOT publish, the deny path silently throws `AuthorizationDeniedException` instead of returning `Result<T, AuthorizationFailure>.Failure(...)`. Add a `[ModuleInitializer]` carrier method with `[DynamicDependency(PublicMethods, typeof(Result<TPayload, AuthorizationFailure>))]` per `TPayload` you use. See [ZA.Mediator.Authorization AOT docs](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/authorization.md#aot-publish). |
```

Match the table's column count + formatting (likely 2-column "issue | mitigation").

**Step 4: Sanity check.**

```powershell
Select-String -Path content/za-clean/AGENTS.md -Pattern "AuthorizationPolicy|\[Authorize\]"
```

Expected: 0 matches (all v1 attribute names purged from AGENTS.md prose).

```powershell
Select-String -Path content/za-clean/AGENTS.md -Pattern "IAuthorizedRequest|DynamicDependency"
```

Expected: at least 1 match each (the new gotcha row).

**Step 5: Commit.**

```powershell
git add content/za-clean/AGENTS.md
git commit -m "chore(docs): add proactive AOT gotcha row for IAuthorizedRequest<TPayload>"
```

---

### Task 8: Push + open PR

**Step 1: Re-verify `ZeroAlloc.Mediator.Authorization 2.0.0` is on NuGet.**

```powershell
$resp = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/zeroalloc.mediator.authorization/index.json"
$resp.versions | Select-Object -Last 8
```

If `2.0.0` is STILL not listed, **STOP** — pushing would just churn CI. Wait for release-please PR #88 on ZA.Mediator to merge + publish.

**Step 2: Final branch diff inspection.**

```powershell
git log --oneline main..HEAD
git diff main..HEAD --stat
```

Expected ~7-8 commits:
- `593e6ce` (or similar) — design doc (already on branch from brainstorm)
- 6 task commits with `chore(deps):` / `chore(docs):` subjects
- 0 commits to `global.json`, 0 to anything outside the template files

All subjects MUST start with `chore(` (or `docs(` if you used `chore(docs)`). NOT `feat:` or `feat!:` — release-please uses the conventional-commit type to compute the version bump.

**Step 3: Push.**

```powershell
git push -u origin feat/template-v2-authorization-migration
```

**Step 4: Open PR.**

```powershell
$body = @'
## Summary

Migrates the `za-clean` template in-place to consume `ZeroAlloc.Authorization 2.0.0` + `ZeroAlloc.Mediator.Authorization 2.0.0`. New apps scaffolded from `dotnet new za-clean` now get the v2 shape — `[Policy]` / `[RequirePolicy]` attributes, async-only `IAuthorizationPolicy.EvaluateAsync`, two-call DI (`AddZeroAllocAuthorization()` + `WithAuthorization()`).

## Changes

- `Directory.Packages.props`: bump `ZeroAlloc.Authorization 1.2.2 → 2.0.0`, bump `ZeroAlloc.Mediator.Authorization 4.1.4 → 2.0.0` (split-versioning starts at 2.0.0), drop `ZeroAlloc.Mediator.Authorization.Generator` pin (bundled into ZA.Authorization v2)
- `MyApp.Application.csproj`: drop the `.Generator` PackageReference
- `OrdersPolicies.cs`: `[AuthorizationPolicy("X")]` → `[Policy("X")]`; rewrite `bool IsAuthorized(...)` to single async `EvaluateAsync(...)` returning `ValueTask<UnitResult<AuthorizationFailure>>`. Sync-completing wrap in `new ValueTask<...>(syncResult)` — allocation-free.
- `CreateOrderCommand.cs` + `GetOrderByIdQuery.cs`: `[Authorize("X")]` → `[RequirePolicy("X")]`
- `Program.cs`: add `services.AddZeroAllocAuthorization();` before `services.AddMyAppApplication(...)` to satisfy the v2 D3 startup guard
- `AGENTS.md`: proactive gotcha row for `IAuthorizedRequest<TPayload>` AOT trim concern

## Versioning

Patch bump only — the template's CLI/manifest surface is unchanged, only the pinned-deps shape inside scaffolded apps shifts. All commit subjects use `chore(deps):` / `chore(docs):` to drive release-please toward a patch bump (not minor/major).

## Test plan

- [x] `dotnet new za-clean` scaffolds clean against v2 packages locally
- [x] Scaffolded app: `dotnet build` green, `dotnet test` green (all integration tests pass including `AuthorizationTests`)
- [x] AOT publish smoke (linux-x64 on CI; Windows local verified for non-AOT)
- [x] No `[Authorize]` / `[AuthorizationPolicy]` references remain in template source or AGENTS.md
- [x] CI gates (`build`, `real-run-smoke`, `aot-publish-smoke`) all green

## Design reference

[docs/plans/2026-05-20-template-v2-authorization-migration-design.md](docs/plans/2026-05-20-template-v2-authorization-migration-design.md)
'@

gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Templates --title "chore(deps): migrate za-clean template to ZA.Authorization v2 + Mediator.Authorization v2" --body $body --base main
```

**Step 5: Watch CI.**

Three jobs to watch:
- `build` — should pass (template pack + smoke)
- `real-run-smoke` — should pass (HTTP-level POST /orders end-to-end)
- `aot-publish-smoke` — should pass (linux-x64 native publish + healthz check)

Most likely failures:
- **NU1605 / restore failure**: `ZeroAlloc.Mediator.Authorization 2.0.0` isn't on NuGet yet. STOP and wait for it to publish.
- **`real-run-smoke` returns 403 instead of 201**: D3 guard threw because `AddZeroAllocAuthorization()` ordering is wrong, OR the policy rewrite missed something. Inspect the test output's stderr for `InvalidOperationException` traces.
- **`aot-publish-smoke` fails at runtime**: AOT trim stripped something we needed. Capture the failure mode — should NOT happen for plain `IRequest<T>` requests, but if it does, may need a Program.cs-level `[DynamicDependency]`.

If anything fails, pull the failing-job log via `gh run view <runId> --log --job <jobId>` and iterate.

---

## Notes for the executor

- **DRY:** the per-file commit pattern is intentional. Combining all file changes into one commit makes the diff harder to review and risks a single commit subject release-please interprets incorrectly.
- **YAGNI:** don't add `[DynamicDependency]` annotations to the template's `Program.cs` even though we documented the AOT requirement — the template's existing requests are all `IRequest<T>` (throw path), not `IAuthorizedRequest<T>` (Result path). The AOT trim concern doesn't bite today. Adding annotations preemptively is dead code.
- **TDD:** the verification step (Task 6) IS the test. There are no unit tests for template source — the test is "does a scaffolded app build + pass its own tests + AOT-publish cleanly."
- **Conventional-commit scope is mandatory.** Every commit subject starts with `chore(deps):` or `chore(docs):` so release-please attributes this as a patch bump. **Never `feat:` or `feat!:`** — that would push a wrong version.
- **One commit per task.** No batching. The plan's task boundaries match release-please's commit-by-commit changelog generation.
- **Subagent dispatch hint:** Tasks 1-2 (csproj + props) are fully mechanical; Tasks 3-5 are source rewrites; Task 6 is verification; Task 7 is docs; Task 8 is push+PR. A reasonable batching is: Tasks 1-5 in one dispatch (additive package + source changes), Task 6 standalone (verification), Task 7 standalone (docs), Task 8 by controller (PR open + watch).
