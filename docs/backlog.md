# ZeroAlloc.Templates — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## ~~B1 — Fix za-vertical-slice WritePipelineBench schema-collision crash~~ — ✅ shipped 2026-05-28

**Shipped:** Dropped the `Database.EnsureCreated()` call from `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`'s `ConfigureServices` callback. `Program.cs`'s startup `MigrateAsync()` is now the single schema source — no race, no `SqliteException: 'table "Customers" already exists'`. The three previously-NA'd benchmarks (`PlaceOrder_FullPipeline`, `PlaceOrder_MediatorDirect`, `PlaceOrder_HandlerDirect`) now produce real numbers under the `Benchmarks (manual)` workflow.

**Diagnosis (durable record):** Two competing schema-creation paths fought at startup — `Program.cs`'s `MigrateAsync()` and the bench's `EnsureCreated()`. Whichever fired second hit "table already exists". Pure deletion fix; the in-memory SQLite connection's lifetime is unaffected (it lives in `_connection` and is shared via `UseSqlite(_connection!, ...)`).

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
