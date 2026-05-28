# ZeroAlloc.Templates — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## B1 — Fix za-vertical-slice WritePipelineBench schema-collision crash

**What.** The benchmark project crashes during `[GlobalSetup]` with `SqliteException: SQLite Error 1: 'table "Customers" already exists'`. All three benchmarks (`PlaceOrder_FullPipeline`, `PlaceOrder_MediatorDirect`, `PlaceOrder_HandlerDirect`) report `NA` in the BDN output. The `dotnet run` exit code stays 0 because BDN considers itself successful when individual benchmarks fail — so CI passes but no useful numbers are produced.

**Why.** Surfaced 2026-05-28 during the first triggered run of the `Benchmarks (manual)` workflow_dispatch CI. Two competing schema-creation paths fight at startup:

- `Program.cs` calls `db.Database.MigrateAsync()` (applies migrations)
- `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs:76` calls `Database.EnsureCreated()` inside `WithWebHostBuilder` → `ConfigureServices`

Whichever fires second sees the tables already exist. The exception bubbles up because EF Core's migration tracker doesn't recognise the schema as "applied" (no `__EFMigrationsHistory` rows).

**Sketch.** Drop the `EnsureCreated()` call from the bench's `ConfigureServices` callback. Let `Program.cs`'s `MigrateAsync` be the single schema source — its `MigrationsAssembly` configuration on line 70 is already wired correctly. The in-memory SQLite connection lives in the bench's `_connection` field and is shared with the WebApplicationFactory via the explicit `UseSqlite(_connection!, ...)` call, so removing `EnsureCreated` doesn't break the shared-connection invariant.

**Tradeoff / risks.** Pure deletion. Risk: if a future change strips the migration assembly or the seed step, the bench fails differently. Mitigated by the existing `MigrationsAssembly` config.

**Graduation signal.** When a maintainer wants real WritePipeline numbers for za-vertical-slice — currently blocked. The other three benchmark legs (za-clean Primitives, za-clean WritePipeline, za-vertical-slice Primitives) produce clean numbers and are published in [`docs/za-clean.md`](za-clean.md#benchmarks).

---

## B2 — Postgres bench profile for WritePipeline (better signal-to-noise)

**What.** The `WritePipelineBench` for both templates runs against in-memory SQLite (`DataSource=:memory:`). The NBomber load test (file-backed SQLite) was capped at 473 RPS in the original measurements, dominated by SQLite's single-file lock. The BDN `WritePipeline` row similarly underrepresents ZA framework cost because EF + SQLite I/O dominates the per-request budget.

**Why.** A Postgres-backed bench profile would surface the framework cost more clearly — Postgres's per-statement latency is more predictable and concurrent reads don't lock. Two benchmark profiles per template (one SQLite, one Postgres) would let adopters compare apples-to-apples while seeing how the data-layer choice moves the numbers.

**Sketch.**

- Add a second bench profile in `MyApp.Benchmarks.WritePipelineBench`, either via `[Params]` for the DB backend or via two separate `[Benchmark]` methods.
- Provision Postgres in `.github/workflows/benchmarks.yml` via `services: postgres:17` on the matrix job.
- Use `testcontainers-dotnet` for local-dev parity, or document `docker run postgres:17` as the local setup.
- Update `docs/za-clean.md` with a comparison row — "WritePipeline (SQLite): X ms / Y KB; WritePipeline (Postgres): X' ms / Y' KB. The delta is dominated by EF tracking + Postgres protocol overhead, not ZA framework cost."

**Tradeoff / risks.** ~30-60 min of workflow plumbing (Postgres service container, connection-string config, secrets/env wiring) plus the bench code itself. CI run time grows ~30s per leg. Worth it if the templates' published numbers are meant to be representative — currently they're SQLite-bound, which obscures the ZA framework cost narrative.

**Graduation signal.** First adopter who asks "what are the Postgres numbers?" Or proactive: pair with the next za-clean perf-related change. Depends on B1 (the za-vertical-slice WritePipelineBench has to run cleanly before adding a second profile is meaningful).

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
