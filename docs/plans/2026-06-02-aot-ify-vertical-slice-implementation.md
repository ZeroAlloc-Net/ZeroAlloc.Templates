# AOT-ify `za-vertical-slice` Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Enable `<PublishAot>true</PublishAot>` in `content/za-vertical-slice/src/MyApp/MyApp.csproj` by eliminating the two remaining reflection sites in Program.cs — mirroring the hand-list pattern that already AOT-publishes successfully in za-clean. Closes B5 in `docs/backlog.md`.

**Architecture:** Add a `Common/MyAppServiceCollectionExtensions.cs` wrapper that hand-lists `services.AddScoped<IRequestHandler<TReq, TResp>, THandler>()` for each of the six existing slices (mirror of za-clean's `AddMyAppApplication(...)`). Replace the reflective endpoint walk in Program.cs with six explicit `XxxEndpoint.Map(app)` calls. Flip the AOT toggle and update the "Add a use case" AGENTS.md recipe to capture the two new manual steps. No new tests; existing IntegrationTests + the `aot-publish-smoke-vs` + `real-run-smoke-vs` CI gates are the regression net.

**Tech Stack:** ZeroAlloc.Mediator (already at 4.1.4), .NET 10, `<PublishAot>true</PublishAot>`, conventional commits + release-please.

**Reference design doc:** `docs/plans/2026-06-02-aot-ify-vertical-slice-design.md` (committed `3d16e0b`).

**Working branch:** `design/b5-aot-ify-vertical-slice` (already created off `main`).

> **Note on TDD shape:** This is a template-internal mechanical refactor, not a new-behavior feature. The TDD "write failing test first" step doesn't apply — every change is verified by existing tests + CI gates (compile, integration tests, AOT publish, AOT-binary boot-and-curl-healthz). Each task's verification is "build + targeted tests + optionally local AOT publish."

---

### Task 1: Add `Common/MyAppServiceCollectionExtensions.cs`

**Files:**
- Create: `content/za-vertical-slice/src/MyApp/Common/MyAppServiceCollectionExtensions.cs`

**Step 1: Confirm the six existing handler signatures**

Run from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`:

```bash
grep -rn "IRequestHandler<" content/za-vertical-slice/src/MyApp/Features --include="*.cs"
```

Expected output (six lines, one per slice handler):
```
.../CreateCustomer.cs:  : IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>
.../GetCustomer.cs:     : IRequestHandler<GetCustomerQuery, Result<CustomerDto, Error>>
.../PlaceOrder.cs:      : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
.../GetOrder.cs:        : IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>
.../ListOrders.cs:      : IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>
.../CancelOrder.cs:     : IRequestHandler<CancelOrderCommand, UnitResult<Error>>
```

If the signatures differ, **STOP** and update the registration block (Step 2) to match — the closed generics in `AddScoped<IRequestHandler<TReq, TResp>, THandler>()` must be byte-identical to the implements clause.

**Step 2: Create the file**

Create `content/za-vertical-slice/src/MyApp/Common/MyAppServiceCollectionExtensions.cs` with this content verbatim:

```csharp
using MyApp.Authorization;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Features.Orders.CancelOrder;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

public static class MyAppServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZeroAlloc.Mediator pipeline, validation, authorization, and
    /// hand-listed slice handlers. The hand-list pattern is the AOT-friendly
    /// alternative to <c>RegisterHandlersFromAssembly</c> (which uses
    /// reflection-based assembly scanning incompatible with NativeAOT).
    /// Each new slice adds one line to the block below.
    /// </summary>
    public static IServiceCollection AddMyApp(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        services.AddMediator()
                .WithValidation()
                .WithAuthorization(configureAuthorization);

        // Per-slice handler registrations — keep in declaration order matching
        // the Features/<Area>/<UseCase>/ folder tree so adding a slice is
        // visually obvious.
        services.AddScoped<IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>, CreateCustomerHandler>();
        services.AddScoped<IRequestHandler<GetCustomerQuery, Result<CustomerDto, Error>>, GetCustomerHandler>();
        services.AddScoped<IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>, PlaceOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>, GetOrderHandler>();
        services.AddScoped<IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>, ListOrdersHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCommand, UnitResult<Error>>, CancelOrderHandler>();

        return services;
    }
}
```

**Step 3: Verify compile (with the old Program.cs still in place, the new file is just dead code at this point)**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If a using directive doesn't resolve (e.g. `MyApp.Authorization` namespace name doesn't match), **STOP** and fix — the using list must mirror the namespaces of the slice files. Read the failing slice to find the right namespace.

**Step 4: Commit**

```bash
git add content/za-vertical-slice/src/MyApp/Common/MyAppServiceCollectionExtensions.cs
git commit -m "feat(za-vertical-slice): add AddMyApp() hand-listed handler registration

Wraps AddMediator().WithValidation().WithAuthorization() plus six
AddScoped<IRequestHandler<TReq, TResp>, THandler>() registrations — one per
existing slice. The hand-list pattern is the AOT-friendly alternative to
RegisterHandlersFromAssembly's reflection-based scanning. Mirrors za-clean's
AddMyAppApplication(...) shape so both templates share the same AOT posture.

Program.cs swap to consume this lands in the next commit."
```

---

### Task 2: Rewrite Program.cs — mediator wiring + endpoint registration

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs`

**Step 1: Identify the three edit blocks**

```bash
grep -n "RegisterHandlersFromAssembly\|GetTypes()\|using System.Reflection" content/za-vertical-slice/src/MyApp/Program.cs
```

Expected output (line numbers may shift slightly):
```
1:using System.Reflection;
37:    .RegisterHandlersFromAssembly(typeof(Program).Assembly)
240:    .GetTypes()
```

**Step 2: Add the slice using directives at the top of Program.cs**

The hand-list of `XxxEndpoint.Map(app)` calls in Step 4 needs the `*Endpoint` types in scope. Insert these usings alphabetically into the existing `using` block at the top of Program.cs:

```csharp
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Features.Orders.CancelOrder;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;
```

**Step 3: Drop `using System.Reflection;`** (line 1)

The reflective walk being replaced was the only consumer. Drop the line.

**Step 4: Replace the mediator wiring block** (~line 32–39)

Find this block:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();
builder.Services.AddZeroAllocAuthorization();

builder.Services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly)
    .WithValidation()
    .WithAuthorization(o => o.UseAccessor<HttpSecurityContextAccessor>());
```

Replace the `AddMediator().RegisterHandlersFromAssembly(...)` chain with a single `AddMyApp(...)` call:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();
builder.Services.AddZeroAllocAuthorization();

builder.Services.AddMyApp(o => o.UseAccessor<HttpSecurityContextAccessor>());
```

**Step 5: Replace the endpoint walk** (~lines 222–247)

Find the multi-line "Endpoint discovery — runtime assembly walk" comment block AND the `foreach (var endpointType in typeof(Program).Assembly.GetTypes()...)` loop AND the `endpointType.GetMethod("Map", ...).Invoke(null, new object[] { app });` invocation. Replace the **entire block** (~25 lines) with:

```csharp
// Endpoint registrations — one Map call per slice, mirroring the
// Features/<Area>/<UseCase>/ tree. AOT publish requires the explicit calls;
// the pre-1.0 reflective walk was the last AOT-blocker (the other was
// reflection-based RegisterHandlersFromAssembly, now folded into AddMyApp).
PlaceOrderEndpoint.Map(app);
GetOrderEndpoint.Map(app);
ListOrdersEndpoint.Map(app);
CancelOrderEndpoint.Map(app);
CreateCustomerEndpoint.Map(app);
GetCustomerEndpoint.Map(app);
```

Keep the `app.MapHealthChecks("/healthz");` line that follows untouched. Keep `app.Run();` untouched. Keep the `public partial class Program { }` declaration at the very bottom untouched.

**Step 6: Verify compile**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Common failures:
- "The name 'BindingFlags' does not exist" → forgot to drop a line that still referenced `System.Reflection`. Re-grep `grep -n "System.Reflection\|BindingFlags" content/za-vertical-slice/src/MyApp/Program.cs` and remove every stray reference.
- "The name 'XxxEndpoint' does not exist" → using directive missing or wrong namespace. Check the slice file at `content/za-vertical-slice/src/MyApp/Features/<Area>/<UseCase>/<UseCase>.cs` to confirm the namespace.

**Step 7: Run the integration tests (regression net for "endpoint is wired" + "handler is registered")**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj 2>&1 | tail -3
```

Expected: `Passed!  - Failed: 0, Passed: 12, Skipped: 0`.

If any test fails with "404" on a route or "no service has been registered for type IRequestHandler<...>": you missed a slice in either the endpoint hand-list (Step 5) or the handler hand-list (Task 1, Step 2). Cross-check the failing test against both lists.

**Step 8: Run UnitTests + ConventionTests (sanity)**

```bash
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj 2>&1 | tail -3 && \
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj 2>&1 | tail -3
```

Expected: 15/15 + 5/5 green. ConventionTests assert architectural boundaries (no domain types reference Microsoft.EntityFrameworkCore, etc.) and don't touch Program.cs internals; they shouldn't move.

**Step 9: Commit**

```bash
git add content/za-vertical-slice/src/MyApp/Program.cs
git commit -m "feat(za-vertical-slice): replace reflective wiring with explicit AOT-friendly calls

Program.cs collapses six lines of AddMediator()...RegisterHandlersFromAssembly()
plumbing into one builder.Services.AddMyApp(...) call (added in the prior
commit), and replaces the 25-line reflective endpoint walk with six explicit
XxxEndpoint.Map(app) calls. Drops the now-unused 'using System.Reflection;'
import.

Both edits eliminate the assembly-walk reflection that AOT publish refuses
to support. Existing IntegrationTests (12 tests across the six slices) still
pass — they're the regression net for 'every slice's handler is registered
and route is wired.'"
```

---

### Task 3: Flip `<PublishAot>true</PublishAot>` in MyApp.csproj

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/MyApp.csproj`

**Step 1: Read the current PropertyGroup**

```bash
grep -n "PublishAot\|InvariantGlobalization\|TrimmerSingleWarn\|AOT publish intentionally" content/za-vertical-slice/src/MyApp/MyApp.csproj
```

Expected: `InvariantGlobalization` line present; `PublishAot` and `TrimmerSingleWarn` absent; multi-line "AOT publish intentionally NOT enabled" comment block present.

**Step 2: Edit the PropertyGroup**

Open `content/za-vertical-slice/src/MyApp/MyApp.csproj`. Replace the existing 7-line comment block that begins with `<!-- AOT publish intentionally NOT enabled` and the surrounding context. The block ends with `<InvariantGlobalization>true</InvariantGlobalization>`.

Replace:

```xml
<!-- AOT publish intentionally NOT enabled (cf. za-clean which does opt in).
     Two reflection sites in this template still block AOT: assembly-walk
     endpoint discovery in Program.cs and RegisterHandlersFromAssembly()
     in the mediator wiring. Persistence is no longer a blocker post the
     ZA.ORM swap. A future iteration can opt into AOT by source-generating
     the endpoint registry and using explicit per-slice
     AddScoped<IRequestHandler<,>>. -->
<InvariantGlobalization>true</InvariantGlobalization>
```

With:

```xml
<!-- AOT publish enabled. Handler registration is hand-listed in
     Common/MyAppServiceCollectionExtensions.cs (AddMyApp); endpoints are
     hand-listed via XxxEndpoint.Map(app) calls near the end of Program.cs.
     Each new slice adds one line to each list — see AGENTS.md §3. -->
<PublishAot>true</PublishAot>
<TrimmerSingleWarn>true</TrimmerSingleWarn>
<InvariantGlobalization>true</InvariantGlobalization>
```

**Step 3: Verify Debug build (no AOT compilation, just confirms the new properties don't break the build)**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

`<PublishAot>true</PublishAot>` enables AOT analyzer warnings during ordinary build (IL2026, IL3050, etc.) so this step also surfaces any latent trim hazards. The `<TrimmerSingleWarn>true</TrimmerSingleWarn>` collapses per-package warning families into one summary line each.

**Expected warnings post-build:** a small number of `TrimmerSingleWarn` summaries for OpenTelemetry / Sqlite / Npgsql / Microsoft.AspNetCore.Mvc.Testing (already known and gated at runtime in this template). Zero warnings is ideal; one or two TrimmerSingleWarn summaries is acceptable. **STOP and inspect** if you see warning counts >5 or any IL3050 (runtime-code-emit, which AOT refuses).

**Step 4: Run tests (sanity — `<PublishAot>true</PublishAot>` shouldn't change JIT behavior, but verify)**

```bash
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj 2>&1 | tail -3 && \
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj 2>&1 | tail -3
```

Expected: 12/12 + 15/15 green.

**Step 5: Commit**

```bash
git add content/za-vertical-slice/src/MyApp/MyApp.csproj
git commit -m "build(za-vertical-slice): enable <PublishAot>true</PublishAot>

Two prior commits eliminated the AOT-blocking reflection sites — handler
registration hand-listed in Common/MyAppServiceCollectionExtensions.cs and
endpoint registration hand-listed in Program.cs. Flipping the AOT toggle
+ TrimmerSingleWarn (mirrors za-clean's posture) makes 'dotnet publish
-p:PublishAot=true' the production build path; CI's aot-publish-smoke-vs
+ real-run-smoke-vs jobs become genuine AOT gates instead of build smokes.

Closes the original B5 backlog item (docs/backlog.md) — the persistence
half of B5 was already done by the ZA.ORM swap (#152)."
```

---

### Task 4: Update AGENTS.md "Add a new use case" recipe

**Files:**
- Modify: `content/za-vertical-slice/AGENTS.md`

**Step 1: Locate the recipe**

```bash
grep -n "^## 3\.\|^## 5\.\|Add a new use case\|AOT-specific" content/za-vertical-slice/AGENTS.md | head -10
```

Expected: section headers for the "Add a new use case" recipe (§3 typically) and the "AOT-specific gotchas (post-swap)" section (§5). Note the exact line numbers for your edit pass.

**Step 2: Insert two new steps in §3**

Find the existing numbered list of steps in §3 — likely 5 or 6 steps ending with the last existing instruction (e.g. "Add the matching SQL migration file..."). After the final existing step, append:

```markdown
6. **Register the handler.** Add one line to
   `src/MyApp/Common/MyAppServiceCollectionExtensions.cs`:
   ```csharp
   services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
   ```
   Keep the per-slice order matching the `Features/` tree so diffs are
   visually obvious. AOT publish refuses to find handlers via reflection,
   so forgetting this step surfaces at boot under
   `dotnet publish -p:PublishAot=true` — `real-run-smoke-vs` in CI is the
   safety net.

7. **Wire the endpoint.** Add one line to `src/MyApp/Program.cs`:
   ```csharp
   YourEndpoint.Map(app);
   ```
   Per-slice explicit registration is the AOT-friendly replacement for
   the pre-1.0 assembly walk. Forgetting it means the route returns 404
   at runtime — the IntegrationTests for that slice will catch it.
```

**Renumber** any subsequent numbered points if the existing list had a step 6+ that becomes step 8+. (Inspect by reading the file; if §3 ends at "step 5" or "step 6" already, just append.)

**Step 3: Update §5 "AOT-specific gotchas (post-swap)" section**

Find and remove (or rewrite) any sentence in §5 that says AOT publish is intentionally **not** enabled in vs. Replace with prose like:

```markdown
AOT publish is enabled. The two reflection sites that previously blocked
it (the endpoint-discovery walk and `RegisterHandlersFromAssembly`) are
replaced by the hand-listed registrations in
`Common/MyAppServiceCollectionExtensions.cs` and `Program.cs` respectively.
The "Add a new use case" recipe in §3 above lists the two manual steps.
```

Use your judgement on phrasing if the surrounding paragraph has more context — keep the spirit ("how AOT works in this template") without contradicting Task 3's csproj comment.

**Step 4: Commit**

```bash
git add content/za-vertical-slice/AGENTS.md
git commit -m "docs(za-vertical-slice): document the two AOT manual steps in 'Add a use case' recipe

§3 'Add a new use case' gains two new explicit steps — register the handler
in MyAppServiceCollectionExtensions.cs and call YourEndpoint.Map(app) in
Program.cs — that are the post-AOT-enable replacement for the prior
reflective walk. §5 'AOT-specific gotchas' note updated to reflect that AOT
publish is now enabled.

Pairs with the prior three commits that wired AddMyApp(), replaced the
endpoint walk, and flipped <PublishAot>true</PublishAot>."
```

---

### Task 5: Full local verification pass

**Files:** none (verification only)

**Step 1: Full solution build**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet build content/za-vertical-slice/MyApp.slnx 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or a small handful of `TrimmerSingleWarn` summary lines, none blocking).

**Step 2: Full test pass**

```bash
dotnet test content/za-vertical-slice/MyApp.slnx 2>&1 | tail -10
```

Expected: total 32 tests passed (15 Unit + 12 Integration + 5 Convention), 0 failed.

**Step 3 (optional, local only — skip if `vswhere.exe` unavailable): AOT publish smoke**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && dotnet publish content/za-vertical-slice/src/MyApp/MyApp.csproj -c Release -r win-x64 -p:PublishAot=true 2>&1 | tail -10
```

Expected: ILC compiles + native link succeeds; binary lands under `bin/Release/net10.0/win-x64/publish/`. If link fails on `'vswhere.exe' is not recognized`, that's a known local-Windows env block (the same one B6-CLN2 documented). CI's Linux runner has the full toolchain; skip this step and let CI verify.

If ILC produces hard errors (IL3050, IL3056, IL3001 — runtime-codegen sites the AOT compiler refuses), **STOP** and investigate. Trim warnings (IL2026, IL2045) collapsed by `<TrimmerSingleWarn>true</TrimmerSingleWarn>` are acceptable; "errors" are not.

**Step 4: Confirm commit history is clean**

```bash
git log --oneline main..HEAD
```

Expected: four commits, in this order:
1. `feat(za-vertical-slice): add AddMyApp() hand-listed handler registration`
2. `feat(za-vertical-slice): replace reflective wiring with explicit AOT-friendly calls`
3. `build(za-vertical-slice): enable <PublishAot>true</PublishAot>`
4. `docs(za-vertical-slice): document the two AOT manual steps in 'Add a use case' recipe`

If any commit was amended or reordered out of intent, fix before push.

---

### Task 6: Push branch + open PR

**Files:** none (workflow actions)

**Step 1: Push the branch**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates && git push -u origin design/b5-aot-ify-vertical-slice 2>&1 | tail -3
```

Expected: `* [new branch] design/b5-aot-ify-vertical-slice -> design/b5-aot-ify-vertical-slice`.

**Step 2: Open the PR**

```bash
gh pr create --title "feat(za-vertical-slice): enable AOT publish — hand-list handlers + endpoints (closes B5)" --body "$(cat <<'EOF'
## Summary

Closes [B5 — AOT-ify za-vertical-slice](docs/backlog.md) by eliminating the two remaining reflection sites in vs's Program.cs and flipping \`<PublishAot>true</PublishAot>\` on. Mirrors the working hand-list pattern that has been AOT-publishing za-clean since pre-swap.

The persistence-side blockers from B5's original 4-item list were already closed by the ZA.ORM swap (#152). This PR closes the two reflection-side blockers.

## What changes

| File | Change |
|---|---|
| \`Common/MyAppServiceCollectionExtensions.cs\` (new) | \`AddMyApp(configureAuthorization)\` wrapper: AddMediator().WithValidation().WithAuthorization() + six explicit AddScoped<IRequestHandler<TReq, TResp>, THandler>() registrations |
| \`Program.cs\` | Six lines of mediator wiring collapse to one \`AddMyApp(...)\` call; 25-line reflective endpoint walk replaced by six explicit \`XxxEndpoint.Map(app)\` calls; drops \`using System.Reflection;\` |
| \`MyApp.csproj\` | Adds \`<PublishAot>true</PublishAot>\` + \`<TrimmerSingleWarn>true</TrimmerSingleWarn>\`; replaces the 7-line "AOT publish intentionally NOT enabled" comment with a 4-line pointer at the two hand-list sites |
| \`AGENTS.md\` | §3 "Add a new use case" recipe gains two new manual steps; §5 "AOT-specific gotchas" updated to say AOT is now enabled |

## Why this shape

The brainstorm explored four approaches (in-template generator / new upstream package / extension to existing ZA.Mediator.Generator / hand-list). The decisive evidence was discovering that **za-clean already AOT-publishes successfully via hand-list** — same pattern, different file location (\`ApplicationServiceCollectionExtensions.cs\` in the Application assembly). Mirroring it for vs lands in days and matches the existing working precedent.

The upstream-generator option (\`ZeroAlloc.Mediator.Generator\` emitting \`AddGeneratedHandlers()\` to remove the manual hand-list step) is tracked separately as a follow-up brainstorm — it would benefit both templates uniformly when ready and is its own design exercise.

## Test plan

- [x] All existing tests pass locally — 15 UnitTests + 12 IntegrationTests + 5 ConventionTests = 32/32
- [ ] CI \`build-vs\` passes
- [ ] CI \`aot-publish-smoke-vs\` passes (becomes a genuine AOT gate now that \`<PublishAot>true</PublishAot>\` is on)
- [ ] CI \`real-run-smoke-vs\` passes (boots the AOT-published binary, curls \`/healthz\`)

Local AOT publish smoke skipped on Windows due to missing \`vswhere.exe\` (same B6-CLN2 env block documented during the original swap); CI's Linux runner has the full toolchain.

## Design + history

- Design doc: [\`docs/plans/2026-06-02-aot-ify-vertical-slice-design.md\`](docs/plans/2026-06-02-aot-ify-vertical-slice-design.md)
- Implementation plan: [\`docs/plans/2026-06-02-aot-ify-vertical-slice-implementation.md\`](docs/plans/2026-06-02-aot-ify-vertical-slice-implementation.md)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)" 2>&1 | tail -3
```

Expected: a URL like `https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/NNN`.

**Step 3: Monitor CI**

```bash
gh pr checks <PR_NUMBER>
```

Wait for all checks to land. The expected check set:
- `build` (za-clean) — should still pass unchanged (no za-clean files touched).
- `build-vs` — should pass (we verified locally).
- `aot-publish-smoke-vs` — **the new real gate**. If this fails, read the log carefully: any IL3050/IL3056/IL3001 means a reflection site sneaked in or a dependency uses runtime codegen.
- `aot-publish-smoke` (za-clean) — unchanged, should still pass.
- `real-run-smoke-vs` — boots AOT'd binary + curls /healthz. If this fails after publish-smoke passes, the binary built but couldn't start — likely a JsonContext missing for some serialized type, or a handler registration miss that the integration tests didn't cover.
- `real-run-smoke` (za-clean) — unchanged.

**Step 4: Merge once green**

If CI is fully green and you have approval (or `--admin` per the prior session's pattern):

```bash
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Step 5: Update the backlog**

Edit `docs/backlog.md` to strike-through the B5 entry and append `✅ shipped 2026-06-02 (PR #NNN)`. Commit on `main`:

```bash
git checkout main && git pull
# Edit docs/backlog.md — strike-through B5 + add ship marker, mirroring how B1/B2/B3 already look
git add docs/backlog.md
git commit -m "docs(backlog): close B5 — AOT-ify za-vertical-slice shipped (#NNN)"
git push
```

Or skip this step if the project convention is to let release-please close the backlog entry via the next release's commit history. Inspect the existing closed-B entries (B1, B2, B3) to confirm — they look like they were appended to the original entry with a strike-through, not closed via release-please. So this step is needed.

---

## Out of scope (deliberately not in this plan)

- Designing the upstream `ZeroAlloc.Mediator.Generator` extension for `AddGeneratedHandlers()` — separate brainstorm.
- Changing slice structure, request/response shapes, validation, or authorization.
- Performance work beyond the AOT enablement itself.
- Modifying za-clean (already AOT-published; mirror, don't change).
- Adding new tests — existing IntegrationTests are the regression net per the design.

## When the plan is complete

The branch `design/b5-aot-ify-vertical-slice` has four commits, all CI checks pass, the PR is merged, and B5 in `docs/backlog.md` carries a `✅ shipped` marker. Next release-please cycle for templates picks up the `feat:` commits and proposes 0.11.0.
