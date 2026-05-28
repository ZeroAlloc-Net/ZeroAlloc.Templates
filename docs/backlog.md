# ZeroAlloc.Templates — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## ~~B1 — Fix za-vertical-slice WritePipelineBench schema-collision crash~~ — ✅ shipped 2026-05-28

**Shipped:** Dropped the `Database.EnsureCreated()` call from `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`'s `ConfigureServices` callback. `Program.cs`'s startup `MigrateAsync()` is now the single schema source — no race, no `SqliteException: 'table "Customers" already exists'`. The three previously-NA'd benchmarks (`PlaceOrder_FullPipeline`, `PlaceOrder_MediatorDirect`, `PlaceOrder_HandlerDirect`) now produce real numbers under the `Benchmarks (manual)` workflow.

**Diagnosis (durable record):** Two competing schema-creation paths fought at startup — `Program.cs`'s `MigrateAsync()` and the bench's `EnsureCreated()`. Whichever fired second hit "table already exists". Pure deletion fix; the in-memory SQLite connection's lifetime is unaffected (it lives in `_connection` and is shared via `UseSqlite(_connection!, ...)`).

---

## ~~B2 — Postgres bench profile for WritePipeline~~ — ✅ shipped 2026-05-28 (za-vertical-slice only)

**Shipped:** Added `[Params(DbBackend.Sqlite, DbBackend.Postgres)]` dispatch to `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`. SQLite path unchanged (B1 fix preserved); Postgres path creates a fresh `bench_<guid8>` database per process via `NpgsqlConnectionStringBuilder` and applies the EF runtime model via `EnsureCreated()`. `Program.cs` honours a new `Bench:SkipStartupMigrate` config flag so the Sqlite-typed startup migration is bypassed when running against Npgsql. `.github/workflows/benchmarks.yml` gained a `services: postgres:17` block + `POSTGRES_*` env vars. Six real rows landed in [run 26592448470](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26592448470); numbers + interpretation now live in `docs/za-vertical-slice.md`. za-clean replication deferred — graduation signal: if a clean-template adopter asks.

**Diagnosis (durable record):** First CI run after the bench refactor produced NA for all 3 Postgres rows with the EF Core error *"Services for database providers 'Microsoft.EntityFrameworkCore.Sqlite', 'Npgsql.EntityFrameworkCore.PostgreSQL' have been registered in the service provider."* Root cause: the bench's `ConfigureServices` was only removing the `DbContextOptions<AppDbContext>` descriptor before re-adding `AddDbContext(UseNpgsql)`. But `Program.cs`'s original `AddDbContext(UseSqlite)` also registers provider-specific singletons (`IDatabaseProvider`, `IRelationalConnection`, etc.) which survived the descriptor removal — EF Core then refused to register Npgsql's provider services into a container that still held Sqlite's. Fix: strip every descriptor whose service-type `FullName` starts with `Microsoft.EntityFrameworkCore` or `Npgsql.EntityFrameworkCore` *before* re-adding the DbContext. Idempotent: the Sqlite branch re-adds an identical stack. **Lesson for future WAF-style DI overrides:** removing only the options class is not enough; remove the full EF stack and re-add cleanly.

---

## B3 — Postgres bench profile for za-clean (replication)

**What.** Replicate B2's `[Params]` DbBackend dispatch into `content/za-clean/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`. The mechanics are identical to B2; the bench class is simpler (1 method vs 3), so the diff is smaller.

**Why.** If za-clean adopters care about the framework-cost narrative the way vertical-slice's now demonstrates, they'll want the same Postgres row. Until they do, this is speculative.

**Sketch.** Mirror PR #140's changes onto the za-clean tree: add Npgsql `PackageReference` to `MyApp.Benchmarks.csproj`, mirror the `Bench:SkipStartupMigrate` flag into za-clean's `Program.cs` startup-migration block, port the `WritePipelineBench.cs` refactor (including the EF-stack-strip from the B2 diagnosis), benchmarks.yml already has the services block from B2 so no workflow change needed. Fold numbers into `docs/za-clean.md`'s existing benchmarks section.

**Tradeoff / risks.** ~1h of straightforward mirroring once B2 is merged. No new architectural decisions — every choice is "what B2 did". Risk: za-clean's bench is also part of the published numbers in `docs/za-clean.md`, so doubling the row count needs a small narrative adjustment in that doc.

**Graduation signal.** First za-clean adopter who asks "what's the Postgres delta?" — *or* proactive when the next za-clean perf-related PR lands and would benefit from the apples-to-apples Postgres baseline.

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
