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

## ~~B3 — Postgres bench profile for za-clean (replication)~~ — ✅ shipped 2026-05-29 (with cross-template sync)

**Shipped:** Replicated PR #140's Postgres + NBomber work onto `za-clean` AND pulled `za-vertical-slice` up to the same AOT-friendly embedded-script schema pattern in the same PR. Both templates now ship:

- `schema.sql` (Sqlite DDL) + `schema.postgres.sql` (Npgsql DDL) as embedded resources alongside per-provider migrations (`Migrations.Sqlite/` + `Migrations.Postgres/`).
- `Database:Provider` (Sqlite|Postgres) + `Database:SchemaStrategy` (EmbeddedScript|Skip, defaults EmbeddedScript) config keys. PR #140's 3-state SchemaStrategy enum collapsed to 2 — reflection-based runtime EF paths gone from both templates.
- `tools/regen-schema.{sh,ps1}` wrappers that bundle the 4-line `dotnet ef migrations add` + `dotnet ef migrations script` recipe for both providers, including the `EfActiveProvider` env-var dance the MSBuild conditional needs.
- `nbomber-postgres-clean` CI job alongside `nbomber-postgres-vs` (existing job's SUT log artifact renamed to `nbomber-sut-log-vs`).
- za-clean's existing AOT-correctness (`<PublishAot>true</PublishAot>` in `MyApp.Api.csproj`) preserved — zero new EF reflection at runtime in either template.

**Diagnosis (durable record).** Two non-obvious EF Core behaviors bit us:

1. **Same-assembly migration discovery.** With both `Migrations.Sqlite/<timestamp>_InitialCreate.cs` and `Migrations.Postgres/<timestamp>_InitialCreate.cs` in the same `MigrationsAssembly`, `dotnet ef migrations script -- --provider Sqlite` emitted DDL for BOTH migrations (the Sqlite generator translated the Postgres-typed annotations literally, producing duplicate-table CREATE statements). Resolution: MSBuild conditional `<Compile Remove>` in the csproj keyed on `EfActiveProvider`. When the property is set at design-time (`EfActiveProvider=Sqlite dotnet ef ...`), the OTHER provider's migration classes are excluded from compilation; the EF tool sees only one provider's `[Migration]` classes. At runtime in production (property unset), both compile in — harmless because runtime uses `ApplyEmbeddedSchemaAsync`, not `MigrateAsync`.

2. **`dotnet ef -p` aliases to `--project`.** Setting `EfActiveProvider` via `dotnet ef ... -p:EfActiveProvider=X` doesn't work — the EF tool interprets `-p:...` as a project path. Resolution: pass via env var: `EfActiveProvider=X dotnet ef ...`. The `tools/regen-schema.{sh,ps1}` wrappers bake this in.

3. **Snapshot clobber on parallel scaffold.** `dotnet ef migrations add ... --output-dir Migrations.Postgres/` overwrote the existing `Migrations.Sqlite/AppDbContextModelSnapshot.cs` because EF locates the snapshot by class name (`AppDbContextModelSnapshot`) in the migrations assembly, not by folder. Resolution: `git mv` the overwritten file into the right folder + recreate the Sqlite snapshot at its original path with `namespace MyApp.Persistence.Migrations.Sqlite` (or `.Postgres`). The committed state has each folder owning its own correctly-namespaced snapshot.

---

## ~~B4 — NBomber-Postgres mirror to za-clean~~ — ✅ superseded by B3 (same PR)

Bundled into B3's same PR — the underlying SchemaStrategy + embedded-script refactor unified cleanly across both templates, so the NBomber-Postgres work for za-clean shipped alongside B3 rather than as a separate workstream.

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
