# `za-clean` Read-Path Allocation Benchmark Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add a focused BDN `[MemoryDiagnoser]` benchmark for za-clean's `OrderRepository.GetByIdAsync` against in-memory Sqlite, then decide post-run whether to record the result in the README — closing #164 either way (claim demonstrated, or scoped to match the measured floor + carry-forward issue filed).

**Architecture:** Four tasks: (1) add `ReadHotPathBench.cs` to `MyApp.Benchmarks` mirroring `WritePipelineBench`'s Sqlite setup pattern, (2) run the benchmark and capture allocation/op + ns/op, (3) conditionally update the README based on the result, (4) push + PR + admin-merge with `feat:` squash for release-please v0.13.0 bump.

**Tech Stack:** .NET 10 / BenchmarkDotNet 0.15.x / Microsoft.Data.Sqlite / ZeroAlloc.ORM 1.5.0.

**Reference design doc:** `docs/plans/2026-06-03-read-hot-path-bench-design.md` (committed `11118c6` on this branch).

**Working branch:** `feat/za-clean-read-hot-path-bench` (already created off `main` at `c1481ea`).

> **Local SDK pin gotcha** (recurring): `global.json` pins SDK `10.0.300 latestMinor`; dev machine has 10.0.204 max. Before any `dotnet build`/`dotnet test`/`dotnet run`:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before each commit. **Never commit the relaxed pin.**

---

### Task 1: Add `ReadHotPathBench.cs` to `MyApp.Benchmarks`

**Files:**
- Create: `content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs`

**Step 1: Read the existing `WritePipelineBench.cs` for setup-pattern reference**

```
Read: c:\Projects\Prive\ZeroAlloc\ZeroAlloc.Templates\content\za-clean\benchmarks\MyApp.Benchmarks\WritePipelineBench.cs
```

Confirm:
- `ApplyMigrations(IAsyncDbConnection, isPostgres)` helper exists (lines ~193-205) — we'll call its Sqlite branch
- `[MemoryDiagnoser]` attribute usage + Setup/Cleanup pattern
- `Microsoft.Data.Sqlite`, `System.Data.Async.Adapters` (for `.AsAsync()`), `MyApp.Infrastructure.Persistence` namespaces

**Step 2: Create the benchmark file**

```csharp
using System.Data.Async;
using System.Data.Async.Adapters;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using ZeroAlloc.ORM.Migrations;

namespace MyApp.Benchmarks;

/// <summary>
/// Focused read hot-path allocation benchmark for the za-clean ORM layer.
/// Exercises <see cref="OrderRepository.GetByIdAsync"/> directly against an
/// in-memory SQLite connection — no HTTP, no mediator, no serialization.
/// The measured allocation is the "framework hot path" the README claim
/// scopes to (closes #164).
///
/// <para>
/// Setup seeds one Order with two OrderLines so the multi-result-set read
/// path is fully exercised (the `[Query]` SQL is a head + lines join).
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ReadHotPathBench
{
    private SqliteConnection? _conn;
    private IAsyncDbConnection? _async;
    private OrderRepository? _repo;
    private OrderId _seededId;

    [GlobalSetup]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _async = _conn.AsAsync();
        ApplyMigrations(_async);

        _repo = new OrderRepository(_async);

        // Seed a known order with two lines so GetByIdAsync exercises the
        // head + lines multi-result-set path that the [Query] SQL drives.
        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-A", 1, Money.TryCreate(10m, "EUR").Value);
        order.AddLine("SKU-B", 1, Money.TryCreate(5m, "EUR").Value);
        _repo.AddAsync(order, default).GetAwaiter().GetResult();
        _seededId = order.Id;
    }

    [Benchmark]
    public async Task<Order?> GetByIdAsync()
        => await _repo!.GetByIdAsync(_seededId, default).ConfigureAwait(false);

    [GlobalCleanup]
    public void Cleanup() => _conn?.Dispose();

    private static void ApplyMigrations(IAsyncDbConnection conn)
    {
        var source = new EmbeddedResourceMigrationSource(
            typeof(OrderRepository).Assembly,
            "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");
        var runner = new MigrationRunner(conn, source, new SqliteMigrationDialect());
        runner.RunAsync().GetAwaiter().GetResult();
    }
}
```

> **Adapt to actual conventions** found in `WritePipelineBench`:
> - If the using directives use a different style (e.g. fully-qualified types in a few places), match.
> - If the `OrderRepository` constructor / `OrderRepository.AddAsync` / `GetByIdAsync` signatures don't match what's written above, **STOP and re-read** the actual `OrderRepository.cs`. (As of HEAD they take `(IAsyncDbConnection, CancellationToken)` and `(OrderId, CancellationToken)` respectively — the bench should compile against those.)
> - The `[Query]` for `ReadOrderAsync` returns a tuple of `(OrderHeadRow, IReadOnlyList<OrderLineRow>)?` and is private to the repository. The public surface is `GetByIdAsync(OrderId, CancellationToken)` returning `Task<Order?>` — that's what we benchmark.

**Step 3: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj -c Release
```

Expected: green. If the build fails, the most likely culprit is a missing using directive or a type-name mismatch between the bench source and the actual `OrderRepository` public surface. Read carefully and fix; don't push placeholder code.

**Step 4: Verify the `Program.cs` benchmark switcher picks the new class up**

```powershell
type content/za-clean/benchmarks/MyApp.Benchmarks/Program.cs
```

Most BDN harnesses use `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)` which auto-discovers `[MemoryDiagnoser]`-decorated classes. If the Program.cs uses an explicit `BenchmarkRunner.Run<TYPE>(...)` pattern, you may need to add the new class to its dispatch list — STOP and read it, adjust if needed.

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs
git commit -m "feat(za-clean): read-path BDN allocation benchmark (closes #164)

Focused [MemoryDiagnoser] benchmark targeting OrderRepository.GetByIdAsync
directly against in-memory SQLite — no HTTP / mediator / serialization
in the measured path. Backs (or scopes) the README's 'zero-allocation
through the framework hot path' claim with a reproducible number.

Sqlite-only by design. Postgres + vs read-paths are separate concerns
and deferred to follow-ups if useful."
```

---

### Task 2: Run the benchmark and capture results

**Files:** none (measurement only)

**Step 1: Run the benchmark**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
cd content/za-clean
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"
cd ../..
git checkout global.json
```

Expected: BDN runs through warmup → iterations → reports a single result row. Captures `Mean` (ns/op) and `Allocated` (B/op) for the `GetByIdAsync` method.

> **Warmup-time note:** BDN's full pipeline takes ~30-60s. The output table is the deliverable for this task.

**Step 2: Capture the result line**

Look for the table near the end of stdout:

```
|       Method | Mean | Error | StdDev | Allocated |
|------------- |-----:|------:|-------:|----------:|
| GetByIdAsync | ... ns | ... ns | ... ns | ... B |
```

Record the `Mean` and `Allocated` values explicitly in the agent report — they drive the Task 3 decision.

**Step 3: Categorize the result**

Apply the design doc's threshold logic:

- **Tier A (≤ ~300 B):** Result is consistent with `OrderHead + 2 OrderLine` record allocation floor. README claim "zero-alloc through framework hot path" stays accurate. → Task 3 path: update README to record the number.

- **Tier B (300-1000 B):** Result is meaningfully above floor but not catastrophic. Probably driven by some combination of value-type boxing on `@id`, Sqlite provider internal buffers, async state-machine boxes. → Task 3 path: file a ZA.ORM v1.6 issue for provider-typed parameter emit; narrow the README claim ("zero-alloc materialization" or similar precision); commit benchmark without README touch.

- **Tier C (>1000 B):** Result is significantly above floor. Likely indicates a real ZA-side gap. → Task 3 path: STOP, present findings, decide between (a) narrow README, (b) pivot to upstream ORM fix in a separate PR.

**Step 4: No commit yet — proceed to Task 3 with the decision in hand**

---

### Task 3: Conditionally update the README based on Task 2 result

**Files (Tier A only):**
- Modify: `content/za-clean/README.md`

**Tier A path (≤ ~300 B): record the number**

The existing benchmarks table at README lines ~5-14 lists `Framework primitives end-to-end ~125 ns / 160 B` and `End-to-end pipeline ~1.3 ms / 36 KB`. Add a sibling row recording the read-path number:

Use Edit to insert a new table row between the existing primitives line and the end-to-end pipeline line (or wherever the table layout is cleanest — read the file first). The text should be something like:

```
| **Read hot path** (ZA.ORM `GetByIdAsync` / Sqlite) | ~<N> ns / <M> B |
```

Replace `<N>` and `<M>` with the actual numbers from Task 2. Add a short footnote near the existing methodology paragraph noting the bench file (`benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs`).

**Commit (Tier A):**

```powershell
git add content/za-clean/README.md
git commit -m "docs(za-clean): record read-path allocation floor in benchmarks table

Adds <N> ns / <M> B per GetByIdAsync call to the benchmarks table.
This is consistent with the OrderHead + 2 OrderLine record allocation
floor — backs the 'zero-alloc through framework hot path' claim with
a reproducible number (closes #164)."
```

**Tier B path (300-1000 B): file follow-up, narrow claim, no README addition**

1. Create a new ZA.ORM issue:
   ```powershell
   gh issue create --repo ZeroAlloc-Net/ZeroAlloc.ORM \
       --title "feat: provider-typed parameter emit to avoid DbParameter.Value boxing" \
       --body "ZA.ORM emits `cmd.Parameters[i].Value = (object?)expr ?? DBNull.Value;` which boxes value-type parameters per call. Read-path benchmark on za-clean's GetByIdAsync (BDN [MemoryDiagnoser]) shows <M> B/op — meaningfully above the result-object floor.

   Investigate emitting provider-typed parameters (e.g. `new SqliteParameter { Value = id, SqliteType = SqliteType.Integer }`) where the type-shape allows, avoiding the `(object)` round-trip.

   Surfaced by ZA.Templates #164."
   ```

2. Edit the README to narrow the claim. Change line 3 from:
   ```
   Source-generated, zero-allocation through the framework hot path.
   ```
   to:
   ```
   Source-generated, zero-allocation materialization (read-path framework cost ~<M> B/op for value-type parameter bind boxing — tracked in [ZA.ORM #XYZ](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/XYZ)).
   ```

3. Commit:
   ```powershell
   git add content/za-clean/README.md
   git commit -m "docs(za-clean): narrow zero-alloc claim post-measurement (closes #164)

   Read-path BDN allocation benchmark measured <M> B/op on GetByIdAsync,
   meaningfully above the result-object floor. Root cause is DbParameter.Value
   boxing on value-type parameters — ZA.ORM's emit uses `(object?)expr ??
   DBNull.Value` at every binding site.

   Narrow the README claim to 'zero-allocation materialization' precision
   pending ZA.ORM #XYZ (provider-typed parameter emit, v1.6 candidate).
   The benchmark stays in the repo as the reproducible audit."
   ```

**Tier C path (>1000 B): STOP and report**

Don't update the README or file an issue without confirming the next move with the user. Present the number + ask whether to:
- (a) narrow README + file ORM issue (Tier B-style)
- (b) defer README touch + pivot to ORM-side fix in a separate PR before closing #164
- (c) something else

---

### Task 4: Push + PR + admin-merge

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected commits (Tier-dependent):
- Tier A: design + bench + readme = 3 commits
- Tier B: design + bench + readme-narrowing = 3 commits
- Tier C: design + bench = 2 commits (readme deferred)

**Step 2: Final sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-clean/MyApp.slnx -c Release
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -c Release
git checkout global.json
git status
```

Expected: build + tests green, working tree clean.

**Step 3: Push**

```powershell
git push -u origin feat/za-clean-read-hot-path-bench
```

**Step 4: Open the PR**

The PR title + body depends on which Tier we landed in. Below is the Tier A body; adapt for B / C as needed.

```powershell
$prBody = @'
## Summary

Closes #164. Adds a focused BDN [MemoryDiagnoser] benchmark over za-clean''s ORM read hot path (`OrderRepository.GetByIdAsync`) so the README''s "zero-allocation through the framework hot path" claim has a reproducible audit number behind it.

## What changes

- **New benchmark**: `content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs`. Exercises `OrderRepository.GetByIdAsync` against in-memory SQLite, no HTTP / mediator / serialization. Seeds one Order with two OrderLines per `[GlobalSetup]` so the full multi-result-set read is measured.
- **README** (Tier A only): records the measured number in the benchmarks table.

## Result

Measured: **~<N> ns / <M> B per `GetByIdAsync` call** on in-memory SQLite.

<commentary based on Tier — see implementation plan>

## Reproduce

```bash
cd content/za-clean
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"
```

## Scope

- **Sqlite-only by design** — the README claim is about ZA''s framework code, not provider-specific allocations. Postgres + SqlClient have their own provider-allocation characteristics; if useful, a sibling Postgres run can be added later (existing `WritePipelineBench` has the setup pattern).
- **No vs read-path bench** — vs''s read shape is different; separate work if useful.

## Note for release-please

`feat:` commit → minor bump (v0.13.0). **Squash title MUST start with `feat:`** (recurring release-please gotcha).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "feat(za-clean): read-path allocation benchmark (closes #164)" --body $prBody
```

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected check set: `build`, `build-vs`, `real-run-smoke`, `real-run-smoke-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`. The bench file isn't covered by the smoke runs (it's `MyApp.Benchmarks.csproj`, not a target of `real-run-smoke`), but the `build` job will compile the whole solution including the bench project — verify that passes.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

Squash title must start with `feat:`. PR title already does.

**Step 7: Verify post-merge + #164 closure**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
gh issue view 164 --json state -q .state
```

Expected: new squashed `feat(za-clean): ...` commit on top of main; #164 state `CLOSED` (auto-closed by `closes #164`).

**Step 8: Release-please pick-up**

```powershell
Start-Sleep -Seconds 60
gh pr list --state open --search "release-please"
```

Expected: a fresh `chore(main): release ZeroAlloc.Templates 0.13.0` PR opens (or refreshes if one already exists). Capture the PR number; queued for user to merge when ready.

---

## Out of scope

- Postgres read-path benchmark — different provider allocation profile; separate scope
- vs read-path benchmark — different repository shape; separate work under `za-vertical-slice/benchmarks/`
- HTTP-pipeline read benchmark — different scope from "framework hot path"
- Fixing any boxing surfaced (Tier B/C) — separate ZA.ORM v1.6 work

## When the plan is complete

The branch `feat/za-clean-read-hot-path-bench` has 3-4 commits (1 design + 1 bench + optional README + merge squash on main). #164 auto-closed. release-please proposes v0.13.0. If Tier B/C: a new ZA.ORM issue is filed for the provider-typed parameter emit feature.
