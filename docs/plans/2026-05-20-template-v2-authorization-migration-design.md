# `za-clean` Template — v2 Authorization Migration Design

**Date:** 2026-05-20
**Status:** Brainstormed and approved; ready for implementation plan
**Tracks:** Downstream of `ZeroAlloc.Authorization` v2.0.0 (shipped) and `ZeroAlloc.Mediator.Authorization` v2.0.0 (PR #87 merged, PR #88 release-please pending publish)
**Versioning impact:** `ZeroAlloc.Templates` patch bump (template surface unchanged; only pinned deps shift)

## Context

`ZeroAlloc.Authorization` v2.0.0 renamed `[AuthorizationPolicy]` → `[Policy]` and `[Authorize]` → `[RequirePolicy]`, simplified `IAuthorizationPolicy` to a single async `EvaluateAsync(ISecurityContext, CancellationToken)`. `ZeroAlloc.Mediator.Authorization` v2.0.0 deletes the in-package generator, splits versioning from core, and adds a startup-time D3 guard that throws if `services.AddZeroAllocAuthorization()` wasn't called before `WithAuthorization()`.

The `za-clean` template pins v1-era packages and uses the v1 attribute names + 4-method `IAuthorizationPolicy` shape. New apps scaffolded today inherit a deprecated surface.

## Goal

Update `za-clean` in-place to consume the v2 packages cleanly. New apps scaffolded from `dotnet new za-clean` get the v2 shape with no migration overhead. Existing v1-scaffolded apps continue to work (they own their own dependency pins after scaffolding).

## Design decisions

Three decisions locked during brainstorming.

### D1 — Migration strategy: in-place rewrite to v2 only

The template moves cleanly to v2. No manifest branching (`--use-authorization-v1`), no parallel `za-clean-v1` template.

Rejected:
- **Template parameter for v1 vs v2** — high template-engine conditional cost across 6 files; CI test-surface doubles; existing v1 users already have working scaffolded apps and don't typically re-scaffold.
- **Parallel frozen v1 template** — two template packages to maintain forever; same edge-case-only audience as in-place rewrite.

Templates are forward-looking by nature; users wanting v1 today are scaffolding new code into a sunset API. The Microsoft .NET community has handled past breaking changes the same way (no `--minimal-vs-controllers-v6` flag preserved when 7 shipped).

### D2 — Version pin shape: specific (`2.0.0`), not floating

`Directory.Packages.props` pins to `2.0.0` for both `ZeroAlloc.Authorization` and `ZeroAlloc.Mediator.Authorization`. Matches the established convention (current pins are `1.2.2`, `4.1.4` — both specific).

Rejected:
- **Floating minor (`2.*`)** — auto-picks up 2.x minors but defers updates to NuGet's resolver, can surprise users when minor versions ship behavior tweaks.
- **Floored major (`[2.0.0,3.0.0)`)** — same NuGet-resolver-defers concern as floating minor.

Templates are snapshot artifacts. The pinned version is what the scaffolded app starts with; the consumer bumps as they like. Specific pins are predictable.

### D3 — Proactive AOT note in `AGENTS.md`

The template's existing requests are all plain `IRequest<T>` (throw path). The AOT trim concern documented in `ZeroAlloc.Mediator.Authorization` v2 docs only bites if a request uses `IAuthorizedRequest<TPayload>` (Result path). A future developer scaffolding from the template and later switching to `IAuthorizedRequest<T>` would hit a silent throw-vs-Result mismatch under `PublishAot=true`.

Add a row to `AGENTS.md`'s "ZA-specific gotchas" table (~5 lines) explaining the `[DynamicDependency]` requirement and linking to the upstream AOT docs. Saves a hard-to-diagnose debugging session.

Rejected:
- **No proactive note** — only document when a consumer actually hits it. Costs nothing to add a row to an existing gotcha table; the realistic alternative is a future bug report with confused symptoms.

## Architecture

In-place template update — 6 source files + central package pins + AGENTS.md + one-line Program.cs addition. No manifest changes, no parallel template.

```
ZeroAlloc.Templates/
├── content/za-clean/
│   ├── Directory.Packages.props                 (3 pin updates)
│   ├── src/MyApp.Api/
│   │   ├── Program.cs                           (+ services.AddZeroAllocAuthorization();)
│   │   └── Authorization/HttpSecurityContextAccessor.cs   (verify usings, no change expected)
│   ├── src/MyApp.Application/
│   │   ├── MyApp.Application.csproj             (drop ZeroAlloc.Mediator.Authorization.Generator)
│   │   ├── Authorization/OrdersPolicies.cs      (v1 → v2 attrs + async-only EvaluateAsync)
│   │   ├── CreateOrder/CreateOrderCommand.cs    ([Authorize] → [RequirePolicy])
│   │   ├── GetOrderById/GetOrderByIdQuery.cs    ([Authorize] → [RequirePolicy])
│   │   └── ApplicationServiceCollectionExtensions.cs   (verify WithAuthorization wiring)
│   ├── tests/MyApp.IntegrationTests/AuthorizationTests.cs   (verify still builds; HTTP-level assertions unchanged)
│   └── AGENTS.md                                (+ proactive AOT gotcha row)
└── (no template manifest changes)
```

## File-by-file migration

### Source migrations (6 files)

| File | v1 → v2 change |
|---|---|
| `src/MyApp.Application/Authorization/OrdersPolicies.cs` | `[AuthorizationPolicy("OrdersRead")]` → `[Policy("OrdersRead")]` (same for OrdersWrite). Rewrite `bool IsAuthorized(ISecurityContext)` to `ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(ISecurityContext, CancellationToken)`. Sync-completing wrap in `new ValueTask<...>(syncResult)`. |
| `src/MyApp.Application/CreateOrder/CreateOrderCommand.cs` | `[Authorize("OrdersWrite")]` → `[RequirePolicy("OrdersWrite")]` |
| `src/MyApp.Application/GetOrderById/GetOrderByIdQuery.cs` | `[Authorize("OrdersRead")]` → `[RequirePolicy("OrdersRead")]` |
| `src/MyApp.Application/ApplicationServiceCollectionExtensions.cs` | `AddMyAppApplication` internally calls `WithAuthorization(...)` — unchanged signature. Just verify `using` directives resolve against v2. |
| `src/MyApp.Api/Authorization/HttpSecurityContextAccessor.cs` | `ISecurityContextAccessor` still exists in v2. No API change. Verify usings. |
| `tests/MyApp.IntegrationTests/AuthorizationTests.cs` | HTTP-level scope-claim assertions — no Mediator-Authorization API references. Verify builds. |

### v2 policy idiom (the OrdersRead/OrdersWrite migration shape)

```csharp
[Policy("OrdersRead")]
public sealed class OrdersReadPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Claims.TryGetValue("scope", out var scope)
               && (scope.Contains("orders.read") || scope.Contains("orders.write"))
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Missing orders.read scope"));
}
```

### Host wiring (1 file)

`src/MyApp.Api/Program.cs` — add **one line** immediately before `services.AddMyAppApplication(...)`:

```csharp
services.AddZeroAllocAuthorization();   // contract-side registry (generator-emitted)
```

D3 guard in `WithAuthorization()` requires this — the call sequence becomes:
1. `AddZeroAllocAuthorization()` — registers `[Policy]` classes + `AuthorizerFor<T>` dispatchers as scoped (generated)
2. `AddMyAppApplication(...)` — which internally calls `WithAuthorization(auth => auth.UseAccessor<HttpSecurityContextAccessor>())`

Add `using ZeroAlloc.Authorization.Generated;` near the top of Program.cs if needed (the namespace might be pulled in transitively via `ImplicitUsings`).

### Central package pins (`Directory.Packages.props`)

```diff
- <PackageVersion Include="ZeroAlloc.Authorization" Version="1.2.2" />
+ <PackageVersion Include="ZeroAlloc.Authorization" Version="2.0.0" />
- <PackageVersion Include="ZeroAlloc.Mediator.Authorization" Version="4.1.4" />
+ <PackageVersion Include="ZeroAlloc.Mediator.Authorization" Version="2.0.0" />
- <PackageVersion Include="ZeroAlloc.Mediator.Authorization.Generator" Version="4.1.3" />
```

The `.Generator` pin is dropped entirely — v2's generator is bundled inside `ZeroAlloc.Authorization 2.0.0`, no separate package.

### `MyApp.Application.csproj` cleanup

Remove the line `<PackageReference Include="ZeroAlloc.Mediator.Authorization.Generator" />` — package no longer exists in the v2 world.

## AGENTS.md update

Two updates:

1. Sweep prose for v1 attribute names (`[Authorize]` / `[AuthorizationPolicy]`) and rewrite to v2. The recon noted minimal prose use — likely a 1-2 line sweep.

2. Add a new row to the "ZA-specific gotchas" table (lines 88-101):

```markdown
| Switching a request to `IAuthorizedRequest<TPayload>` for Result-style auth | Under AOT publish, the deny path silently throws instead of returning `Result.Failure`. Add a `[ModuleInitializer]` carrier with `[DynamicDependency(PublicMethods, typeof(Result<TPayload, AuthorizationFailure>))]` per TPayload. See [ZA.Mediator.Authorization AOT docs](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/authorization.md#aot-publish). |
```

## CI / smoke gates

The template's `.github/workflows/ci.yml` has three jobs that exercise the auth path. None require workflow changes — they pick up the v2 packages once `Directory.Packages.props` updates.

| Job | What it does | Update needed? |
|---|---|---|
| `build` | Builds + tests the scaffolded template app | No (no source changes in workflow) |
| `real-run-smoke` | Scaffolds fresh template → boots JIT API → mints HS256 JWT with `scope=orders.write` → POST /orders → asserts 201 Created with numeric Id | No (HTTP-level smoke; resilient to internal Mediator-Authorization changes) |
| `aot-publish-smoke` | Scaffolds fresh app → publishes NativeAOT for linux-x64 → boots binary → asserts `/healthz` returns 200 | No (health-check endpoint doesn't traverse auth pipeline; template requests are plain `IRequest<T>` so AOT trim concern doesn't bite) |

**Crucial sanity check before merge:** the `real-run-smoke` job's `POST /orders` exercises the full auth path end-to-end and is the single best regression signal. Must be green.

**Coordination dependency:** `ZeroAlloc.Mediator.Authorization 2.0.0` must be on NuGet before this template PR's CI restore can succeed. Sequence:
1. ZA.Mediator release-please PR #88 merges → publish workflow → `2.0.0` lands on NuGet
2. We open template PR after that publish event
3. Template CI runs: restore succeeds, build green, both smoke gates green

## Versioning + release

Template package gets a **patch bump** (e.g., `x.y.Z+1`). The template's user-facing surface (CLI args, scaffolded file structure, manifest symbols) is unchanged. Only the version pins baked into the scaffolded app shift.

Commit messages drive release-please:
- Use `chore(deps): bump ZeroAlloc.Authorization to 2.0.0 + ZeroAlloc.Mediator.Authorization to 2.0.0` for the central-pins commit
- `feat:` or `feat!:` would trigger minor/major respectively — avoid for this migration

The PR title can be more descriptive (`docs: migrate template to v2 authorization stack`), but COMMIT subjects on the branch should use `chore(deps)` scope.

**Release sequence:**
1. PR #88 merges on ZA.Mediator → triggers release-please publish → `ZeroAlloc.Mediator.Authorization 2.0.0` lands on NuGet
2. We open template PR after the publish event
3. Template CI green → merge → release-please opens its own PR with the patch bump → merge → `ZeroAlloc.Templates x.y.Z+1` publishes

## Out of scope

- Template versioning convention overhaul (release-please owns it)
- A v1 parallel `za-clean-v1` template (rejected D1)
- Any `IAuthorizedRequest<TPayload>` example in the template (template stays on plain `IRequest<T>` + throw path; AOT note covers the future case)
- Any change to template manifest (`.template.config/template.json`) — no symbols added
