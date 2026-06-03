# `za-clean` ReadHotPath Baseline Context — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.Templates `content/za-clean/`, additive (benchmark + docs)
**Closes:** ZA.ORM #113 (as won't-fix / scope-clarification)
**Branch:** `chore/readhotpath-baseline-context` off `main` at `04a61e6` (post-v0.13.1)

## Background

ZA.Templates #164 (closed via PR #176) introduced `ReadHotPathBench` measuring `OrderRepository.GetByIdAsync` at **37 µs / 2.77 KB per call**. I filed ZA.ORM #113 against that result, framing it as "2.77 KB is meaningfully above the ~300 B result-object floor — likely framework boxing + AdoNet.Async overhead — v1.6 candidate for provider-typed parameter emit."

While exploring the v1.6 brainstorm I discovered ZA.ORM already has comparison benchmarks at [`docs/benchmarks/v0.7.0-sqlite-results.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md). Specifically the **MultiResultSetBench** (head + 10 lines via `;`-joined SELECTs):

| Method | Allocated | Alloc Ratio |
|---|---:|---:|
| HandWrittenAdoNet | 2.62 KB | 1.00 |
| ZeroAlloc_ORM | 2.95 KB | 1.13 |

So **ZA.ORM is already within 13%** (0.33 KB) of hand-written ADO.NET on the same shape. The 2.77 KB in za-clean's ReadHotPathBench is right in line — slightly lower than the 10-lines variant because we only seed 2 lines, but the framework overhead is the same ~0.3 KB. The "framework hot path" claim, scoped to mean "≤13% over hand-written ADO.NET," is already true and has been measured + published.

My original triage compared 2.77 KB to the result-object floor (~300 B). That was the wrong comparison — the achievable floor for *any* implementation of "two-statement read + materialize" against in-memory SQLite is hand-written ADO.NET's 2.62 KB. The remaining ~2.4 KB is the SQLite provider + reader state machine + ADO.NET internals, none of which are ZA's framework code.

## Decision

**Close ZA.ORM #113 as won't-fix** — the framework is already at parity. **Add a `HandWrittenAdoNet` baseline benchmark** to za-clean's `ReadHotPathBench` so the published number isn't misread by future readers (the way I misread it). Update the README's read-path table row to show both numbers + the ratio, replacing the link from `ZA.ORM #113` to the authoritative v0.7.0 benchmarks doc.

This converts the templates bench from "a number floating in space" into "a number in context."

## What changes

**Files modified (2):**

1. **`content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs`** — add a `HandWrittenAdoNet` baseline:
   - Rename the existing `GetByIdAsync` benchmark method to `ZeroAlloc_ORM`
   - Add a `HandWrittenAdoNet` benchmark method with `[Baseline = true]` that issues the same `;`-joined head + lines query directly via `SqliteCommand` + `SqliteParameter` + `ExecuteReaderAsync` + `NextResultAsync`, materializing into the same `Order` + `OrderLine` records
   - Add `[Orderer(SummaryOrderPolicy.FastestToSlowest)]` to match ZA.ORM's bench-suite style
   - The setup (seeding one Order + 2 OrderLines) is unchanged

   Reference shape: ZA.ORM's `tests/ZeroAlloc.ORM.Benchmarks/MultiResultSetBench.cs:HandWrittenAdoNet` is the structural template. Adapt to za-clean's domain types (Order/OrderLine + Money via MoneyConverter for storage roundtrip).

2. **`content/za-clean/README.md`** — update the read-path table row:
   - Replace the single-number row with both numbers + ratio
   - Replace the `ZA.ORM #113` link with [ZA.ORM v0.7.0 SqliteResults](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) (the authoritative source)
   - Update the "Reproduce" block comment if needed

   Proposed table row replacement:
   ```markdown
   | **Read hot path** (`GetByIdAsync`, head + 2 lines, Sqlite) | ~37 µs / 2.77 KB (ZA.ORM) vs ~33 µs / 2.4 KB (hand-written ADO.NET) — framework adds ~0.3 KB / 13%, mostly AdoNet.Async wrapper allocations. See [ZA.ORM bench](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) for the full comparison. |
   ```
   (Exact wording adapted to fit the existing table's voice.)

**Issue actions (in ZA.ORM repo, not in this PR):**

3. **Close ZA.ORM #113** with a comment explaining the finding:
   - Link to the v0.7.0 benchmarks doc
   - Note that ZA.ORM is already within 13% of hand-written on this shape
   - Note that the templates bench is being updated to show the comparison context
   - Mark as won't-fix / scope-clarification

## What stays the same

- ZA.ORM unchanged — no emit work, no v1.6 minor bump on this thread
- ReadHotPathBench's existing `GlobalSetup` (seed Order + 2 lines + return seeded id) — unchanged
- The actual measured ZeroAlloc_ORM number — unchanged (~37 µs / 2.77 KB)
- The README narrowed claim ("zero-allocation through the validator + mediator + mapping chain (data-access adds provider-shaped overhead)") — unchanged

## What might still be worth doing later (deliberately not in this PR)

- **Approach B from the brainstorm** — pursue the 0.33 KB / 13% gap via "static-branch on `CanCreateBatch` when provider capability is compile-time known" (per the v0.7.0 doc's own future-work note). Real optimization with a known target. Worth doing IF a v1.6 minor release accumulates other reasons to ship. Filed in ZA.ORM #113's close comment as deferred work.

## Acceptance criteria (from ZA.ORM #113 / za-clean #164)

- [x] Read-path allocation budget meaningfully reduced **OR** the gap is precisely characterized + documented as out-of-scope. **Resolution: characterized.** ZA.ORM is at 1.13× hand-written; the 0.33 KB delta is documented in the v0.7.0 bench doc; the templates README now exposes that ratio for adopters.

## Commit shape

Single `chore:` commit (no behavior change, no new feature, no bug fix — purely benchmark + docs addition):

```
chore(za-clean): add HandWrittenAdoNet baseline to ReadHotPathBench + README context
```

release-please default config doesn't bump for `chore:` types — this rolls up into the next versioned PR. Fine.

## Out of scope

- ZA.ORM emit changes
- vs ReadHotPathBench (vs's PlaceOrder is single-statement; no multi-result-set read path to bench)
- Postgres baseline (existing ZA.ORM benchmarks cover Postgres via Testcontainers; we don't need to mirror that in templates)
