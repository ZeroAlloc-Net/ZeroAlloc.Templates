# `za-clean` ReadHotPath Baseline Context Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add a `HandWrittenAdoNet` baseline benchmark to `ReadHotPathBench` so the published `ZeroAlloc_ORM` number is interpretable in context (ratio to hand-written ADO.NET). Update README + close ZA.ORM #113 as won't-fix.

**Architecture:** Four tasks: (1) refactor `ReadHotPathBench.cs` to a two-method comparison (`HandWrittenAdoNet` baseline + `ZeroAlloc_ORM` renamed from the existing method), (2) run the benchmark + capture both numbers, (3) update the README's read-path row with both numbers + ratio + new link to ZA.ORM's v0.7.0 bench doc, (4) push + PR + admin-merge with `chore:` squash + close ZA.ORM #113 with a comment.

**Tech Stack:** .NET 10 / BenchmarkDotNet 0.15.x / Microsoft.Data.Sqlite / `MoneyConverter` (za-clean's TEXT storage roundtrip).

**Reference design doc:** `docs/plans/2026-06-03-readhotpath-baseline-context-design.md` (committed `8111523` on this branch).

**Working branch:** `chore/readhotpath-baseline-context` (already created off `main` at `04a61e6`).

> **Local SDK pin gotcha** (TWO files): both root `global.json` and `content/za-clean/global.json` pin `10.0.300`. Before any `dotnet` invocation:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> (Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
> ```
> Revert with `git checkout global.json content/za-clean/global.json` before each commit. **Never commit the relaxed pin.**

---

### Task 1: Add `HandWrittenAdoNet` baseline to `ReadHotPathBench`

**Files:**
- Modify: `content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs`

**Reference template:** ZA.ORM's `tests/ZeroAlloc.ORM.Benchmarks/MultiResultSetBench.cs:75-98` — the structural pattern for the `HandWrittenAdoNet` baseline (raw `SqliteCommand` + `ExecuteReaderAsync` + `NextResultAsync` + manual materialize).

**Key adaptation for za-clean:** the hand-written baseline must do the same `MoneyConverter.FromStorage` roundtrip the ZA.ORM emit does, because Total + Price are stored as TEXT (`"<amount>|<currency>"`). Without this, the hand-written baseline would be unfairly cheap.

**Step 1: Read the current `ReadHotPathBench.cs` to confirm the setup shape**

Read the file. Confirm:
- `_conn` (`SqliteConnection`), `_async` (`IAsyncDbConnection`), `_repo` (`OrderRepository`) fields
- `Setup()` seeds an `Order` with `CustomerId(42)` + 2 lines (`SKU-A` @ qty 1 / 10 EUR, `SKU-B` @ qty 1 / 5 EUR)
- `_seededId` field captures the inserted Order's id (after the prior #166 refactor, `_seededId = (await _repo.AddAsync(order, default)).Id`)
- Current `[Benchmark]` method is `GetByIdAsync()` returning `Task<Order?>`

**Step 2: Refactor the file**

Two changes:

**(a) Add `[Orderer(SummaryOrderPolicy.FastestToSlowest)]`** to the class to match ZA.ORM's bench style. The current attribute is just `[MemoryDiagnoser]`.

Use Edit:

- `old_string`:
  ```csharp
  [MemoryDiagnoser]
  public class ReadHotPathBench
  ```
- `new_string`:
  ```csharp
  [MemoryDiagnoser]
  [Orderer(SummaryOrderPolicy.FastestToSlowest)]
  public class ReadHotPathBench
  ```

You'll also need to add the using:
- `using BenchmarkDotNet.Order;`

Use Edit on the using block to insert it in alphabetical position (after `using BenchmarkDotNet.Attributes;`).

**(b) Rename the existing `GetByIdAsync` benchmark to `ZeroAlloc_ORM` and add a `HandWrittenAdoNet` baseline above it.**

The new shape:

```csharp
[Benchmark(Baseline = true)]
public async Task<Order?> HandWrittenAdoNet()
{
    await using var cmd = _conn!.CreateCommand();
    cmd.CommandText =
        "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
        "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;";
    var p = cmd.CreateParameter();
    p.ParameterName = "@id";
    p.Value = _seededId.Value;
    cmd.Parameters.Add(p);

    await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    if (!await reader.ReadAsync().ConfigureAwait(false))
    {
        return null;
    }
    var customerId = reader.GetInt32(0);
    var status = reader.GetString(1);
    var totalStr = reader.GetString(2);

    await reader.NextResultAsync().ConfigureAwait(false);
    var lines = new List<OrderLine>(capacity: 2);
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
        lines.Add(new OrderLine(
            reader.GetString(0),
            reader.GetInt32(1),
            MoneyConverter.FromStorage(reader.GetString(2))));
    }

    return Order.Materialize(
        _seededId,
        new CustomerId(customerId),
        Enum.Parse<OrderStatus>(status),
        MoneyConverter.FromStorage(totalStr),
        lines);
}

[Benchmark]
public async Task<Order?> ZeroAlloc_ORM()
    => await _repo!.GetByIdAsync(_seededId, default).ConfigureAwait(false);
```

> **Adapt the column-quoting style** to match the existing migrations + repository SQL — they use `"Quoted"` identifiers (already in the snippet above, mirroring `OrderRepository.cs:91-93`).

> **`CreateCommand()` source:** the hand-written baseline uses `_conn` (`IAsyncDbConnection`) via the wrapper because that's the same connection the repository uses, so the comparison stays apples-to-apples for the in-memory-Sqlite-state. (ZA.ORM's `MultiResultSetBench` uses the raw `_raw` SqliteConnection directly; either works since they share state.)

> **`OrderLine` + `Order.Materialize` use the post-#166-refactor shapes** — `OrderLine(string Sku, int Quantity, Money Price)` positional record; `Order.Materialize(OrderId, CustomerId, OrderStatus, Money, IEnumerable<OrderLine>)` factory.

Use Edit to replace the existing `[Benchmark]` block:

- `old_string`:
  ```csharp
      [Benchmark]
      public async Task<Order?> GetByIdAsync()
          => await _repo!.GetByIdAsync(_seededId, default).ConfigureAwait(false);
  ```
- `new_string`: (the two new benchmark methods above)

**Step 3: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
(Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
dotnet build content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj -c Release
```

Expected: green.

If you see CS errors about missing usings, the hand-written baseline likely needs:
- `using System.Data.Async;` (already present from existing code)
- `using Microsoft.Data.Sqlite;` (already present)
- `using MyApp.Domain;` (for `Order`, `OrderLine`, `OrderStatus`)
- `using MyApp.Domain.ValueObjects;` (for `CustomerId`, `OrderId`, `Money`)
- `using MyApp.Infrastructure.Persistence;` (for `MoneyConverter`, `OrderRepository`)

Check the file's existing using directives — if some are missing, add them.

**Step 4: Revert + commit**

```powershell
git checkout global.json content/za-clean/global.json
git add content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs
git commit -m "chore(za-clean): add HandWrittenAdoNet baseline to ReadHotPathBench

Mirrors ZA.ORM's MultiResultSetBench shape: a [Baseline = true]
HandWrittenAdoNet that issues the same head + lines ;-joined SELECT
via raw SqliteCommand + ExecuteReaderAsync + NextResultAsync + manual
materialize. Does the same MoneyConverter.FromStorage roundtrip for
Total + Price so the comparison is apples-to-apples (both paths pay
the TEXT-to-Money decode).

Existing GetByIdAsync benchmark renamed to ZeroAlloc_ORM. Adds
[Orderer(SummaryOrderPolicy.FastestToSlowest)] to match ZA.ORM's
bench-suite style.

Run + numbers in the next commit."
```

---

### Task 2: Run the benchmark + capture both numbers

**Files:** none (measurement only)

**Step 1: Run**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
(Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
cd content/za-clean
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"
cd ../..
git checkout global.json content/za-clean/global.json
```

Expected: BDN runs the two benchmarks (`HandWrittenAdoNet` baseline + `ZeroAlloc_ORM`), reports a two-row summary table.

> **Warmup:** ~30-60s total. The output table is the deliverable.

**Step 2: Capture the results**

Record from the summary table:
- `HandWrittenAdoNet` — Mean (µs), Allocated (B), Alloc Ratio (should be `baseline`)
- `ZeroAlloc_ORM` — Mean (µs), Allocated (B), Alloc Ratio (the key number)

Expected based on the ZA.ORM v0.7.0 bench doc's MultiResultSetBench data: `ZeroAlloc_ORM` should be **~1.10-1.20× hand-written allocations** (e.g. hand-written ~2.4 KB, ZA.ORM ~2.77 KB → ratio ~1.15×). If the ratio is dramatically higher (e.g. >1.5×), STOP — something unexpected is happening; report.

**Step 3: No commit yet — Task 3 uses the captured numbers to update the README**

---

### Task 3: Update README with both numbers + ratio + corrected link

**Files:**
- Modify: `content/za-clean/README.md`

**Step 1: Locate the current "Read hot path" row**

Use Read on `content/za-clean/README.md`. Find the benchmarks table row added by PR #176 — currently looks like:

```markdown
| **Read hot path** (ZA.ORM `GetByIdAsync` / Sqlite, head + 2 lines) | ~37 µs / 2.77 KB — provider + parameter-boxing dominate; tracked in [ZA.ORM #113](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/113) |
```

**Step 2: Replace the row with the comparison-shaped version**

Use Edit:

- `old_string`:
  ```markdown
  | **Read hot path** (ZA.ORM `GetByIdAsync` / Sqlite, head + 2 lines) | ~37 µs / 2.77 KB — provider + parameter-boxing dominate; tracked in [ZA.ORM #113](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/113) |
  ```
- `new_string` (fill in `<ZA_NS>`, `<ZA_KB>`, `<HW_NS>`, `<HW_KB>`, `<RATIO>` from Task 2's measurement):
  ```markdown
  | **Read hot path** (`GetByIdAsync` / Sqlite, head + 2 lines) | ~<ZA_NS> µs / <ZA_KB> KB (ZA.ORM) vs ~<HW_NS> µs / <HW_KB> KB (hand-written ADO.NET) — framework <RATIO>× allocations, ~<DELTA> KB delta is AdoNet.Async wrapper overhead. See [ZA.ORM v0.7.0 benchmarks](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) for the broader comparison (single-row read, multi-row read, multi-result-set, insert × hand-written / Dapper.AOT / ZA.ORM). |
  ```

If the wording feels too long for the table layout, trim — the key elements are: (a) both numbers, (b) the ratio, (c) the link to ZA.ORM's authoritative bench doc.

**Step 3: Update the "Reproduce" block if needed**

Find the existing reproduce comment for the read-path bench. The command is unchanged (`--filter "*ReadHotPathBench*"`), but the comment may say "ZA.ORM read-path allocation floor" — consider rewording to "ZA.ORM vs hand-written ADO.NET — read-path comparison." Small touch, optional.

**Step 4: Verify the markdown renders cleanly**

```powershell
type content/za-clean/README.md | Select-Object -First 35
```

Sanity-check the table — verify column count, no broken pipes, link URL is intact.

**Step 5: Commit**

```powershell
git add content/za-clean/README.md
git commit -m "docs(za-clean): record HandWrittenAdoNet baseline + ratio in README read-path row

After running the new comparison bench: hand-written ADO.NET
<HW_KB> KB vs ZA.ORM <ZA_KB> KB (ratio <RATIO>×). The framework
adds <DELTA> KB / <RATIO_PCT>% — consistent with ZA.ORM's own
v0.7.0 MultiResultSetBench measurement (1.13× hand-written) and
right in line with the broader bench suite there.

Replaces the misleading 'ZA.ORM #113 (gap unresolved)' framing
from PR #176. The link now points at the authoritative v0.7.0
bench doc, where adopters can see ZA.ORM benchmarked across the
full workload matrix (single-row read / multi-row read / multi-
result-set / insert × hand-written / Dapper.AOT / ZA.ORM)."
```

(Substitute the actual numbers in the commit body.)

---

### Task 4: Push + PR + admin-merge + close ZA.ORM #113

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected commits:
1. `8111523` docs(design): add HandWrittenAdoNet baseline to ReadHotPathBench + close ZA.ORM #113
2. `<TASK_1>` chore(za-clean): add HandWrittenAdoNet baseline to ReadHotPathBench
3. `<TASK_3>` docs(za-clean): record HandWrittenAdoNet baseline + ratio in README read-path row

(3 commits total on top of `main`.)

**Step 2: Final sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
(Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
dotnet build content/za-clean/MyApp.slnx -c Release
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -c Release
git checkout global.json content/za-clean/global.json
git status
```

Expected: build green, 9+10+4 = 23 tests pass, working tree clean.

**Step 3: Push**

```powershell
git push -u origin chore/readhotpath-baseline-context
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes ZA.ORM #113 as won't-fix (the framework is already at parity). Adds a `HandWrittenAdoNet` baseline benchmark to `ReadHotPathBench` so the published `ZeroAlloc_ORM` number is interpretable in context, and updates the README's read-path row to show the comparison.

## Why won't-fix on ZA.ORM #113

ZA.ORM already has comparison benchmarks at [`docs/benchmarks/v0.7.0-sqlite-results.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md). Specifically, MultiResultSetBench (head + 10 lines) shows ZA.ORM at **1.13× hand-written ADO.NET allocations** (2.95 KB vs 2.62 KB). The za-clean ReadHotPathBench''s 2.77 KB for head + 2 lines is right in line — ZA.ORM is **already within 13%** of the achievable floor for that workload.

My original #113 filing compared 2.77 KB to the ~300 B result-object floor (Order + 2 OrderLine records), which was the wrong comparison — the floor for any implementation of "head + lines roundtrip via in-memory SQLite" is hand-written ADO.NET''s ~2.4 KB. The framework adds ~0.3 KB / 13%, mostly AdoNet.Async wrapper overhead.

## What changes

- **`ReadHotPathBench.cs`**: refactored from one `[Benchmark]` to two. Adds `HandWrittenAdoNet` (`[Baseline = true]`) issuing the same `;`-joined head + lines SELECT via raw `SqliteCommand` + `ExecuteReaderAsync` + `NextResultAsync` + manual materialize. Does the same `MoneyConverter.FromStorage` roundtrip for Total + Price so the comparison is fair (both paths pay the TEXT-to-Money decode). Existing `GetByIdAsync` benchmark renamed to `ZeroAlloc_ORM`.
- **README**: read-path table row now shows both numbers + the ratio + a link to ZA.ORM''s authoritative v0.7.0 bench doc. Replaces the misleading "ZA.ORM #113 (gap unresolved)" framing from PR #176.

## Measurement

Two-method BDN comparison (Sqlite in-memory, single Order + 2 lines, [MemoryDiagnoser]):

- HandWrittenAdoNet: ~<HW_NS> µs / <HW_KB> KB (baseline)
- ZeroAlloc_ORM: ~<ZA_NS> µs / <ZA_KB> KB (<RATIO>×)

ZA.ORM is at <RATIO>× hand-written allocations, consistent with the v0.7.0 MultiResultSetBench measurement (1.13×).

## Test plan

- [x] Build green
- [x] All existing tests pass (9 unit + 10 integration + 4 architecture = 23/23)
- [ ] CI: build + build-vs + real-run-smoke + real-run-smoke-vs + aot-publish-smoke + aot-publish-smoke-vs

## Note for release-please

`chore:` commit. Default release-please config doesn''t bump for `chore:` types — this change rolls up into the next versioned PR.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "chore(za-clean): add HandWrittenAdoNet baseline to ReadHotPathBench + README context" --body $prBody
```

(Substitute the actual `<HW_*>`, `<ZA_*>`, `<RATIO>` numbers from Task 2.)

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected: all 6 checks green.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

Squash title starts with `chore(za-clean):` — release-please default won't trigger a release PR. Fine.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

**Step 8: Close ZA.ORM #113 with the closing comment**

```powershell
gh issue close 113 --repo ZeroAlloc-Net/ZeroAlloc.ORM --comment "$(cat <<'EOF'
Closing as won't-fix / scope-clarification.

The motivating measurement (ZA.Templates ReadHotPathBench at 2.77 KB) was framed against the result-object floor (~300 B), but the achievable floor for that workload is hand-written ADO.NET. The existing v0.7.0 bench suite at [docs/benchmarks/v0.7.0-sqlite-results.md](../blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) already documents this:

| Shape | ZA.ORM | Hand-written ADO.NET | Alloc Ratio |
|---|---|---|---|
| Single-row read | 1.34 KB | 1.20 KB | 1.12× |
| Multi-row read (1000 rows) | 86.52 KB | 86.13 KB | 1.00× |
| Multi-result-set (head + 10 lines) | 2.95 KB | 2.62 KB | 1.13× |
| Insert | 1.23 KB | 1.18 KB | 1.04× |

ZA.Templates' ReadHotPathBench has been updated to include a HandWrittenAdoNet baseline (PR #<TEMPLATES_PR> over there), confirming the same ~1.13× ratio for the head + 2 lines variant. The ~0.3 KB delta is AdoNet.Async wrapper overhead, not parameter-boxing or generator inefficiency.

**Deferred (future v1.6+ candidate, not blocking):** the v0.7.0 doc's own future-work note mentions \"emit a static branch when the provider's CanCreateBatch capability is compile-time known\" — that could shave a few bytes off the MultiResultSet shape's ratio. Worth doing if a v1.6 release accumulates other reasons to ship; not worth a dedicated minor on its own.
EOF
)"
```

(Substitute `<TEMPLATES_PR>` with the actual PR number from Step 4.)

**Step 9: Final report**

- Final test counts (unit/integration/architecture)
- Bench numbers (`HandWrittenAdoNet` mean+alloc, `ZeroAlloc_ORM` mean+alloc, ratio)
- Templates PR URL/number + merge SHA
- ZA.ORM #113 final state (`CLOSED`)
- Anything unexpected

Do NOT push fixes blindly to CI failures. Investigate first.

---

## Out of scope (deliberately not in this plan)

- ZA.ORM emit changes (Approach B from the brainstorm — defer to a future v1.6 candidate if other reasons accumulate)
- Postgres baseline (out of scope; existing ZA.ORM benchmarks cover Postgres)
- vs read-path bench (vs is single-statement; no multi-result-set read shape to bench)

## When the plan is complete

The branch `chore/readhotpath-baseline-context` has 4 commits (1 design + 1 bench + 1 README + merge squash on main). ZA.ORM #113 is `CLOSED`. README + bench tell the same story coherently: ZA.ORM is at parity with hand-written ADO.NET within 13%; the framework hot-path claim holds for what "framework" means in practice.
