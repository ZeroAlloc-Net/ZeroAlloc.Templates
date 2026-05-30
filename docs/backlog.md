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

## B5 — AOT-ify za-vertical-slice (`<PublishAot>true</PublishAot>`)

**What.** Turn on `<PublishAot>true</PublishAot>` in `content/za-vertical-slice/src/MyApp/MyApp.csproj` and resolve the three reflection blockers the csproj's own comment (lines 9-18) currently defers. End state: both shipped templates publish NativeAOT in production, mirroring `za-clean`'s posture.

**Why.** NativeAOT is the product thesis for `ZeroAlloc.Templates`. Having one template ship JIT-only is an architectural inconsistency, not a deliberate variant — the vs csproj comment already frames it as deferred debt ("A future iteration can opt into AOT by source-generating the endpoint registry…"). PR #145's open-model benchmark made the cost of leaving it deferred concrete: vs's apparent throughput win over za-clean (4,312 vs 4,068 RPS, p50 48ms vs 2,146ms) was JIT-vs-AOT, not framework-vs-framework — once vs goes AOT it'll regress toward clean's shape, which is the honest comparison adopters need to see.

**Sketch.**

1. **Endpoint discovery.** Replace `Program.cs`'s assembly-walk-for-static-`Map`-methods with a source-generated `_Generated.EndpointRegistrations.Map(app)` call. The ZA.Rest source-gen pattern is the template — emit one `Map` invocation per `*Endpoint` static class found at compile time. Falls back gracefully when no endpoints exist (empty generated method, no crash).

2. **Mediator handler registration.** Drop `services.AddMediator().RegisterHandlersFromAssembly(typeof(Program).Assembly)`. Either (a) hand-write per-slice `services.AddScoped<IRequestHandler<TReq, TResp>, THandler>()` calls (mechanical — slices already exist as discrete files), or (b) source-generate the registry the same way as (1). Recommend (b) so adding a slice doesn't require a DI-wiring edit.

3. **EF Core compiled model.** Copy za-clean's pattern: `opts.UseModel(AppDbContextModel.Instance)` for the Sqlite branch; **skip `UseModel` on the Postgres branch** (the compiled model is Sqlite-flavored — see B3 diagnosis iteration 4 and `InfrastructureServiceCollectionExtensions.cs` for the rationale comment). Regenerate the compiled model via `dotnet ef dbcontext optimize` whenever the schema changes; gate the regeneration step in `tools/regen-schema.{sh,ps1}`.

4. **EF LINQ-to-SQL read path.** Replace `GetOrderHandler`'s `db.Orders.AsNoTracking().FirstOrDefaultAsync(...)` with raw ADO.NET, matching `OrderRepository.GetByIdAsync` in za-clean (head + lines in one batched command, `@id` parameter, double-quoted identifiers for Postgres case-fold compat). Vertical-slice doesn't have a repository layer, so the raw SQL lives directly in the handler. INSERT path (`AddAsync` + `SaveChangesAsync`) stays — EF inserts work under AOT once the compiled model is in place.

5. **Validate.** AOT-publish the bench's SUT and run NBomber-Postgres against it. Expected: vs converges toward za-clean's read profile (~4K RPS at high p50). Update `docs/za-vertical-slice.md` with the post-AOT numbers and remove any "JIT-only / faster reads" claims that the current docs may still carry.

**Tradeoff.** The vs read-throughput "win" we just measured disappears. That's the point — leaving it JIT-only let vs cheat on the comparison. The win in cold-start (typical AOT 30-100ms vs JIT 500-1500ms for a CRUD API) and trimmed-binary size more than offsets the per-request hit for the NativeAOT use case the product targets. Adopters who genuinely want JIT-mode (better LINQ ergonomics, easier debugging) can still flip `PublishAot` off — but the **default ships AOT**.

**Connection-hold side effect.** The `db.Database.GetDbConnection() + OpenAsync` pattern that both AOT'd templates will share has a known throughput cost under open-model load (longer Npgsql pool-slot hold than EF's open-on-execute). This becomes a both-templates concern post-B5, so the natural follow-up is **B6 (TBD): drop the manual connection-acquire idiom in favor of a scoped `NpgsqlConnection` factory** — pays dividends in both templates simultaneously. Not blocking B5; just call out the relationship.

**Graduation signal.** Open the PR when source-generators for endpoint discovery + handler registration are designed (a brainstorming session worth its own pass — the ZA.Rest pattern is the prior art but the registration shape differs).

---

## B6 — Drop the manual `db.Database.GetDbConnection() + OpenAsync` idiom in raw-SQL read paths

**What.** Replace the `var conn = db.Database.GetDbConnection(); await conn.OpenAsync(...); ... await conn.CloseAsync();` pattern in `content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs:31-51` with a connection-lifecycle approach that opens-on-execute and closes-on-reader-dispose — mirroring how EF Core's own command pipeline manages connections. Vertical-slice will inherit the same idiom once B5 lands and its read path moves from EF LINQ to raw ADO.NET, so the fix needs to be template-agnostic.

**Why.** The investigation on PR #145 ([comment 4582084724](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/145#issuecomment-4582084724)) showed the current pattern is the dominant reason za-clean's read p50 is 44× higher than za-vertical-slice's (2,146ms vs 48ms) under 5K RPS open-model injection. EF's `IRelationalConnection` opens just-in-time per command and releases the moment the reader disposes — the Npgsql pool slot is held only for the network round-trip. The manual pattern grabs the slot at method entry and holds it across `CreateCommand → ExecuteReaderAsync → ReadAsync → NextResultAsync → ReadAsync → Dispose → CloseAsync` — a 2-3× longer residency. Under open-model load that compounds linearly into queue depth: clean's effective ceiling on a co-located 2-vCPU runner sits around ~2,500 RPS, builds a ~10K in-flight queue at 5K injection, and ~9,780 requests get NBomber-cancelled at scenario end (status code `-100`). vs measured p50 48ms with **zero** failures purely because EF was managing the connection lifecycle for it.

**Sketch.** Three approaches, in recommended order:

1. **(Preferred) Let EF own the connection via `db.Database.OpenConnectionAsync()` + `Database.CloseConnectionAsync()`.** Those wrappers go through `IRelationalConnection`, so the ref-counted open-on-execute / release-on-dispose contract activates. Minimum diff — same `var conn = db.Database.GetDbConnection()`, but `OpenConnectionAsync` instead of `conn.OpenAsync`. Verify the contract on Npgsql 10's `IRelationalConnection` (the ref-count needs to actually hand the slot back to the pool when the reader disposes; a quick BDN micro-bench against the current pattern proves it).

2. **Scoped `NpgsqlConnection` factory, registered as `IDbConnectionFactory`.** Each scoped request rents directly from Npgsql's pool. Cleaner separation from EF (no shared `DbContext.Database` state), and lets the EF context stay narrow to its own command stream. Slightly more wiring (DI registration, factory interface, AOT-trim hint).

3. **`NpgsqlBatch` native batching.** Npgsql 10 supports `NpgsqlBatchCommand` natively, which would also let us drop the hand-built `";"`-joined SQL in `ReadOrderAsync`. Bigger refactor — worth pairing with (2) if we go that direction.

Validate with a re-run of `nbomber-postgres-clean` after the change. Expected outcome: clean's read p50 drops to within 2× of vs's (the residual gap is the actual extra-row materialization work — the manual-acquire delta is the easy 80%).

**Tradeoff.** Approach (1) is a near-zero-risk swap that should claw back most of the latency gap with ~5 lines of code change. (2) and (3) are more idiomatic but a larger surface — premature if (1) closes the gap. Going straight to (3) without measuring (1) first violates YAGNI.

**Graduation signal.** Pick this up **after B5 lands** (so the fix lands in both templates' read paths in the same PR — they'll share the raw-ADO.NET shape post-B5). Block B6 on B5 to avoid a half-state where only za-clean has the fix.

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
