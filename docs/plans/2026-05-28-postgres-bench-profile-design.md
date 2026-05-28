# Postgres bench profile for za-vertical-slice WritePipelineBench — design

**Status:** approved 2026-05-28
**Backlog item:** B2 (docs/backlog.md)
**Scope:** `za-vertical-slice` only this round; `za-clean` deferred to a follow-up if numbers prove informative.

## Goal

Add a Postgres-backed profile to `za-vertical-slice`'s `WritePipelineBench` so the three pipeline-attribution benchmarks (`PlaceOrder_FullPipeline`, `PlaceOrder_MediatorDirect`, `PlaceOrder_HandlerDirect`) can be measured against Postgres in addition to in-memory SQLite. Surfaces the ZA framework cost more clearly by replacing single-process-locked SQLite I/O with predictable per-statement Postgres latency.

## Approach

### Bench shape

Add a `[Params(DbBackend.Sqlite, DbBackend.Postgres)]` enum to the existing `WritePipelineBench` class. BDN cross-products `Method × DbBackend` → 6 result rows per run (3 benchmarks × 2 backends) in one artifact, side-by-side comparison rows.

Alternatives rejected:
- *Separate bench class* (`WritePipelineBenchPostgres`): keeps the existing Sqlite numbers untouched in diffs, but produces two artifacts that need manual side-by-side merging in docs. BDN-native `[Params]` produces the comparison table for free.
- *Two CI matrix legs sharing one class via env-driven dispatch*: adds workflow complexity and breaks the "one bench class, one artifact" mental model.

### Schema strategy

- **Sqlite path:** unchanged. `Program.cs`'s `MigrateAsync()` remains the single schema source (B1 fix preserved — no `EnsureCreated()` in the bench).
- **Postgres path:** `EnsureCreated()` at `[GlobalSetup]`. The template ships only Sqlite-specific migrations; the EF runtime model emits Postgres DDL directly via EnsureCreated. No new migrations folder, no maintenance burden when entities change. Each bench process gets a fresh DB name (e.g., `bench_<guid8>`) so concurrent runs don't collide.

The B1 schema-collision risk doesn't recur — what B1 fixed was two paths *racing* against the same database. The Postgres bench owns its schema-creation path exclusively.

### Package additions

`Npgsql.EntityFrameworkCore.PostgreSQL` 9.x added to `Directory.Packages.props` (root, `content/za-clean`, `content/za-vertical-slice`) and referenced from `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj` only. Production `MyApp.csproj` stays Sqlite-only — the README's "swap to Postgres" recipe is unchanged.

### Per-class lifecycle

- `[GlobalSetup]` (once per backend value):
  - **Sqlite:** open `SqliteConnection("DataSource=:memory:")`, build `WebApplicationFactory<Program>` with overridden `DbContextOptions<AppDbContext>` pointing at the connection. `Program.cs`'s `MigrateAsync` runs on app startup.
  - **Postgres:** generate fresh DB name `bench_<guid8>`, create it via `CREATE DATABASE` against the default `bench` DB, build `WebApplicationFactory<Program>` with overridden options pointing at the new DB, call `db.Database.EnsureCreated()` to apply the runtime model.
- Iteration body: unchanged — `_client.PostAsJsonAsync("/orders", _httpRequest)` for FullPipeline, mediator/handler invocation for the other two.
- `[GlobalCleanup]`: dispose factory + client + Sqlite connection (Sqlite branch) or `DROP DATABASE bench_<guid8>` (Postgres branch).

Iterations accumulate rows just like the existing Sqlite path — apples-to-apples, no truncation between iterations.

## CI workflow

Add `services: postgres:17` to the `benchmark` job in `.github/workflows/benchmarks.yml`. Service containers attach to every matrix leg in GHA (no per-leg `services` syntax), so the 3 non-Postgres legs spin up an unused container. Trade-off considered:

- **A. Accept the waste** (chosen): ~150MB pull (cached after first run) + ~3s startup per leg, negligible against a 30-min benchmark budget. Single workflow file.
- **B. Split into `benchmark` + `benchmark-pg` jobs**: cleaner intent, double the YAML surface area. Rejected.

```yaml
services:
  postgres:
    image: postgres:17
    env:
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: bench
    ports:
      - 5432:5432
    options: >-
      --health-cmd pg_isready
      --health-interval 5s
      --health-timeout 3s
      --health-retries 5
```

Env vars exposed to the bench step: `POSTGRES_HOST=localhost`, `POSTGRES_PORT=5432`, `POSTGRES_DB=bench`, `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgres`. Bench reads with defaults so local-dev with default `docker run` "just works".

**Runtime impact:** `za-vertical-slice / WritePipeline` leg goes from ~2.5 min → ~6 min (twice the row count + Postgres I/O per iteration). Still well under the 30-min `timeout-minutes`.

## Local-dev

Recipe documented in `WritePipelineBench.cs`'s class-level XML doc (visible where the dev lands when reading the bench source):

```text
# Postgres profile rows require a local Postgres on 5432 with
# user/password postgres/postgres and a writable database 'bench'.
# Start one:
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
  --name bench-pg postgres:17
# Run the benches:
dotnet run -c Release -- --filter "*WritePipelineBench*"
# Cleanup:
docker stop bench-pg
```

`testcontainers-dotnet` rejected: adds a NuGet dep, requires Docker daemon for any bench run, and diverges from the CI service-container path. The "external Postgres" assumption keeps CI and local-dev paths identical.

## Error handling

Hard-fail on Postgres unreachable at `[GlobalSetup]`. Connection failure surfaces as `NpgsqlException` from `EnsureCreated`; we don't catch — BDN reports the failure and the dev sees the env-var/docker-run hint in the message. Silently skipping Postgres rows on connection-failure was rejected — it'd produce a half-populated table no one notices.

## Docs

Create `docs/za-vertical-slice.md` (currently nonexistent — the template has been live since v0.7 with no dedicated doc page). Paste the Postgres + Sqlite comparison table from the first post-merge CI run, plus a short interpretation paragraph framing the framework-cost narrative.

Doc-paste happens *after* the bench-profile PR merges and the manual workflow runs on `main` (same flow as PR #133 refreshing numbers after PR #131 added the workflow). Out of scope for the bench-profile PR itself.

## Risks / out-of-scope

- Bench numbers are localhost-Postgres-dominated (network stack + WAL flushes). Absolute numbers will be higher than Sqlite; the *deltas* between FullPipeline / MediatorDirect / HandlerDirect are what we care about for the framework-cost story.
- `za-clean` replication deferred to a follow-up. Graduation signal: if the Postgres deltas in vertical-slice reveal a story worth telling for clean too.
- All non-WritePipeline matrix legs (Primitives × 2 templates + za-clean / WritePipeline) get an unused Postgres service container. Accepted overhead.

## Graduation

B2 closes when:
1. PR adding the bench profile + workflow service container merges to `main`.
2. Manual `Benchmarks (manual)` workflow run on `main` produces 6 real rows for `za-vertical-slice / WritePipeline`.
3. `docs/za-vertical-slice.md` lands with the comparison table and interpretation paragraph.
