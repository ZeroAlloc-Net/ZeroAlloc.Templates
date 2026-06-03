# `za-clean` Read-Path Allocation Benchmark — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.Templates `content/za-clean/benchmarks/`, additive
**Closes:** #164
**Branch:** `feat/za-clean-read-hot-path-bench` off `main` at `c1481ea` (post-v0.12.1)

## Background

za-clean's README at line 3 claims:

> Source-generated, zero-allocation through the framework hot path.

The benchmarks table further specifies "0 B framework cost" for primitives and "~125 ns / 160 B (= mapping cost alone; chain adds 0 B)" for the write pipeline — quoting the 160 B as a caller-shape allocation (record + nested array) that "every framework pays."

Issue #164 surfaces a specific risk: **`DbParameter.Value` is typed `object`**, so a value-type parameter bind (e.g. `@id int`) boxes unless the generator emits provider-typed parameters. ZA.ORM's emitted code uses `(object?)expr ?? DBNull.Value` at every `.Value =` site (verified during the v1.5 transaction work) — this *does* box value types per parameter.

Whether the boxing matters depends on its actual size relative to the result-object floor. **The fastest way to find out is to measure.** Right now no benchmark exists for the read path.

## Decision

Add a focused **read hot-path** allocation benchmark (`ReadHotPathBench`) that exercises `OrderRepository.GetByIdAsync(orderId)` directly against in-memory Sqlite, without ASP.NET / mediator / serialization overhead. `[MemoryDiagnoser]` reports the actual allocated bytes per call. The number is the verdict:

- **If allocation ≤ expected result-object floor** (~200–300 B for `OrderHead + 2 OrderLine` records + their internal `Money` VOs): the README claim holds, file the parameter-box question as a ZA.ORM v1.6 candidate (not blocking).
- **If allocation > expected floor by a meaningful margin**: either narrow the README claim to "zero-alloc materialization" precision, or pivot to a ZA.ORM-side fix (provider-typed parameter emit) in a separate PR.

Either way, after this PR adopters can reproduce the number and audit the claim.

## What changes

**Files created (1):**

- `content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs` — new BDN benchmark targeting the ZA.ORM read path in isolation. Setup mirrors `WritePipelineBench`'s Sqlite path (in-memory connection, migrations applied via `MigrationRunner` against the Sqlite-embedded SQL files). Skips the HTTP / ASP.NET layer entirely — constructs `OrderRepository` directly and seeds one Order with two OrderLines, then benchmarks `GetByIdAsync` against the seeded id.

Shape:

```csharp
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

(The migration-apply pattern is lifted verbatim from `WritePipelineBench.ApplyMigrations` to keep the repo consistent.)

**Files modified (1, conditional):**

- `content/za-clean/README.md` — IF the benchmark result is ≤ ~300 B (consistent with the result-object floor), add a row to the benchmarks table reporting it. IF the number is meaningfully larger, leave README alone and file the boxing as a ZA.ORM v1.6 follow-up issue (separate PR will either fix it or narrow the claim).

The README touch is a judgement call to be made **after running the benchmark**, not committed sight-unseen.

## Scope of "framework hot path"

The benchmark deliberately bypasses ASP.NET, mediator, validation, and serialization — those layers introduce their own allocations and are not what the README claim is about. The benchmark targets exactly what "framework hot path" should mean: the ZA.ORM-emitted code from `OrderRepository.GetByIdAsync` through Sqlite reader iteration into materialized `Order` + `OrderLine` records. Any allocation observed is either:

(a) The result objects themselves (expected — the floor)
(b) The Sqlite provider's internal allocations (out of ZA scope; document as "provider cost")
(c) ZA's own framework code (boxing, internal buffers, etc. — this is what the claim is about)

The benchmark numbers don't separate (a)/(b)/(c) automatically — we'll reason about them post-run.

## Commit shape

Two commits if README touch happens:

1. `feat(za-clean): read-path BDN allocation benchmark (closes #164)`
2. `docs(za-clean): record read-path allocation floor in README benchmarks table` (only if results justify it)

Or one commit if the number reveals a gap and we defer the README question entirely:

1. `feat(za-clean): read-path BDN allocation benchmark (closes #164)` — benchmark only, README question deferred to follow-up

Squash title: `feat:` so release-please cuts a minor bump (v0.13.0 — new benchmark surface).

## What stays out of scope

- **Postgres variant of the read bench** — Postgres has its own provider allocation behavior; existing `WritePipelineBench` has the Postgres setup if we want to add it later. YAGNI for the README question this PR closes.
- **vs read-path bench** — different shape (single-statement); separate work under `za-vertical-slice/benchmarks/`.
- **ZA.ORM-side fix for any boxing found** — separate ORM-side PR (Approach B from the brainstorm). Tracked as a v1.6 candidate after the measurement is in.
- **End-to-end HTTP `GET /orders/{id}` bench** — out of "framework hot path" scope; the existing `WritePipelineBench` already shows what end-to-end overhead looks like; a sibling bench for the read would be useful but is separate.
- **`OrderRepository.CountAsync` / `GetAllAsync`-style benchmarks** — only `GetByIdAsync` is in scope for the README claim verification.

## Risk

- **Measurement is the verdict**. If the number surprises us (much larger than expected, or much smaller), the right next step depends on the actual data. The PR ships the measurement regardless; the README touch is a judgement call after seeing results.
- **CI bench costs**: the existing `benchmarks.yml` workflow probably runs all benchmarks. Adding one more iteration costs ~1-2 min CI wall time; acceptable.
