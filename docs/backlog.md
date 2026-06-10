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

## ~~B5 — AOT-ify za-vertical-slice (`<PublishAot>true</PublishAot>`)~~ — ✅ shipped 2026-06-02 (#161)

**Shipped.** Both templates now NativeAOT publish. The four-blocker list got resolved in two phases: the two persistence blockers (EF compiled model + EF LINQ-to-SQL read path) were eliminated by the [ZA.ORM swap](#b6--ef-core--zaorm-11-swap--shipped-2026-06-01); the two reflection blockers (endpoint discovery walk + `RegisterHandlersFromAssembly`) were closed by PR #161 using the **hand-list pattern that za-clean was already running**, not the source-generated registry the original sketch recommended.

The original §1–§2 recommendation was "source-generate the endpoint registry + handler registration." During brainstorming, discovering that za-clean already AOT-publishes via the simpler hand-list approach (one extension method, `[Scoped]` attribute on each handler, generated-by-ZA.Inject concrete-type registry) changed the design. The hand-list pattern ships in days; the upstream-generator approach was its own meaningful library project. The hand-list now ships in both templates; the upstream generator is tracked as a separate brainstorm if and when the manual one-line-per-slice friction proves real.

**What landed:**

- `content/za-vertical-slice/src/MyApp/Common/MyAppServiceCollectionExtensions.cs` — new `AddMyApp(...)` extension wrapping `AddMediator().WithValidation().WithAuthorization()` + six `IRequestHandler<,>` registrations + `AddMyAppServices()` (ZA.Inject-generated concrete-type registry).
- `[Scoped]` (`using ZeroAlloc.Inject;`) added to every handler class. **Surprise step the design missed:** ZA.Mediator's source-generated dispatch resolves handlers by *concrete type* (`GetRequiredService<TConcreteHandler>`), not by the `IRequestHandler<,>` interface — so without the `[Scoped]` attribute the dispatch fails at runtime. Caught by IntegrationTests; documented as the third manual step in AGENTS.md §3.
- `Program.cs` — six lines of mediator wiring collapsed to one `AddMyApp(...)` call; 25-line reflective endpoint walk replaced by six explicit `XxxEndpoint.Map(app)` calls; `using System.Reflection;` dropped.
- `MyApp.csproj` — `<PublishAot>true</PublishAot>` + `<TrimmerSingleWarn>true</TrimmerSingleWarn>` on; AOT-deferral comment replaced with a pointer at the hand-list sites.
- `AGENTS.md` — §3 "Add a new use case" recipe gains three new manual steps (mark handler `[Scoped]`, register the interface in `MyAppServiceCollectionExtensions`, call `XxxEndpoint.Map(app)` in `Program.cs`). §5 "AOT publish" rewritten — was "intentionally not enabled," now "enabled."

**Tradeoff acknowledged.** Original §6 noted the vs read-throughput "win" over za-clean would shrink once vs went AOT — the open-model NBomber numbers in PR #145 reflected JIT-vs-AOT, not framework-vs-framework. Bench refresh is a follow-up task; bench numbers on `main` are pre-AOT-enable and should be re-captured for the comparison to land honestly. Carry-forward.

**Diagnosis (durable record).** The original B5 sketch over-engineered: it recommended source-generating the endpoint registry + handler registration to preserve auto-discovery DX. During brainstorming we discovered za-clean *already AOT-publishes* via a hand-list pattern that's mechanically smaller — one extension method per assembly, `[Scoped]` attributes on each handler. Mirroring that pattern landed in four implementation commits over a few hours; the upstream source-generator alternative would have been weeks of work for a marginal DX win. The hand-list adds a "one line per slice" step, which AGENTS.md now makes explicit and CI's `real-run-smoke-vs` catches if forgotten.

---

## ~~B5 (original entry, preserved for context)~~ — AOT-ify za-vertical-slice (`<PublishAot>true</PublishAot>`)

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

## ~~B6 — EF Core → ZA.ORM 1.1 swap~~ — ✅ shipped 2026-06-01

**Shipped:** Both templates now use [ZeroAlloc.ORM](https://github.com/MarcelRoozekrans/ZeroAlloc.ORM) 1.1.0 + [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) 1.3.0 + raw `Microsoft.Data.Sqlite` / `Npgsql` providers in place of EF Core 10. Zero `Microsoft.EntityFrameworkCore.*` references remain in either template's source tree. `dotnet new za-clean` + `dotnet new za-vertical-slice` both pack, scaffold, build, and test green end-to-end (48 tests passing across both scaffolded outputs).

Headline changes:

- `Persistence/AppDbContext.cs`, `Persistence/CompiledModel/`, `Persistence/Configurations/`, `Persistence/Migrations.{Sqlite,Postgres}/`, `DesignTimeDbContextFactory.cs` — all deleted across both templates.
- Repositories become source-generated `public sealed partial class XxxRepository(IAsyncDbConnection conn)` with inline `[Query]` / `[Command]` partials. za-clean's `OrderRepository.cs` dropped from 117 lines of hand-rolled hold-the-slot ADO.NET to 70 lines of declarative SQL annotations. za-vertical-slice's 6 feature handlers each became `public sealed partial class XxxHandler(IAsyncDbConnection conn)` with co-located `[Query]` / `[Command]` partials — perfect vertical-slice fit.
- Schema is now folder-scoped embedded SQL: `Persistence/Migrations/Sqlite/NNN_description.sql` + `Persistence/Migrations/Postgres/NNN_description.sql`. ZA.ORM's `MigrationRunner` applies them at startup against the `__zaorm_migrations` history table. The custom `ApplyEmbeddedSchemaAsync` helpers in both `Program.cs` files are gone. `tools/regen-schema.{sh,ps1}` removed.
- Lifetime: production wiring registers `IAsyncDbConnection` per scope. Test fixtures register a kept-alive singleton wrapping an open `:memory:` Sqlite connection. Migration apply happens once in each test fixture's ctor.

**Supersedes B5 (AOT-ify za-vertical-slice).** Two of B5's four blockers evaporate under the ZA.ORM swap:
- *EF Core compiled model* (B5 step 3) — N/A; no DbContext to model.
- *EF LINQ-to-SQL read path* (B5 step 4) — already in ZA.ORM-emitted code. ListOrders uses `IAsyncEnumerable<T>`.

vs's path to AOT now needs only B5's source-generated endpoint discovery (step 1) and handler registration (step 2).

**Supersedes the deferred B6 (drop manual connection-acquire idiom).** ZA.ORM's generator-emitted ref-counted lifecycle (open-on-execute, close-on-reader-dispose) replaces the hold-the-slot pattern entirely. The Npgsql pool-slot-hold concern B5 surfaced as a follow-up is structurally resolved.

**Lessons captured (durable record):**

> **Upstream status (2026-06-09):** All five lessons have been addressed in ZA.ORM —
> the two genuine feature-requests landed (#101, #102) and the three docs items
> made it into the ZA.ORM cookbook. Lessons preserved here for historical record.

1. **ZA.ORM 1.1 partial-method accessibility.** The generator emits implementations as `public`, so `[Query]` / `[Command]` declarations must be `public partial`, never `private`. Nested row records must match (CS0050 fires if a `public` method references a `private` record type). ✅ **Filed + fixed upstream as [ZeroAlloc.ORM#101](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/101)** — generator now emits accessibility-matching implementations.
2. **`CommandKind.Identity` does NOT auto-append the provider's identity syntax.** User-authored SQL must include `RETURNING "Id"` (or `SCOPE_IDENTITY()` / `LAST_INSERT_ROWID()` per provider). ✅ **Documented upstream** in `docs/cookbook/commands.md` and `docs/cookbook/bulk-insert.md` — the contract is now explicit.
3. **`Task<IReadOnlyList<T>>` is NOT supported as a bare top-level partial return.** Only inside a tuple for multi-result-set queries. ✅ **Filed + fixed upstream as [ZeroAlloc.ORM#102](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/102)** — bare list returns now supported alongside `Task<List<T>>` and `Task<IList<T>>`. v1.6.0 also added composite-row recursion inside list result sets.
4. **DI scope-disposal trap for shared `IAsyncDbConnection` in test fixtures.** Registering a kept-alive wrapper as **scoped** (via factory delegate) makes `Microsoft.Extensions.DependencyInjection` add it to the scope's disposable list. End of the first request scope calls `DisposeAsync` on the wrapper, which closes the underlying `SqliteConnection` and evaporates the `:memory:` database. Second request fails with "no such table". Fix: register as **singleton** in test fixtures — singleton-by-factory still tracks for disposal, but only at host shutdown. `SqliteConnection` tolerates the resulting double-dispose. ✅ **Pattern documented upstream** in the ZA.ORM cookbook (testing recipes).
5. **`IAsyncDbConnection.CreateCommand()` returns `IAsyncDbCommand`, not `System.Data.Common.DbCommand`.** Tests asserting against the database directly should match the async surface. ✅ **Documented upstream** in the ZA.ORM AdoNet.Async cookbook.

**Carry-forward items:**

> **Status check (2026-06-10):** all four B6-CLN carry-forwards are now resolved.
> CLN1's stale CI numbers were replaced by a fresh `docs/benchmarks/capacity-recipe.md`
> run on 2026-06-10 (post-#197 output caching).

- **B6-CLN1 — Benchmark refresh.** ✅ **Shipped.** Both READMEs now carry capacity-recipe numbers from the 2026-06-10 single-laptop run (i9-12900HK, Postgres-pinned, SUT-pinned). Raw NBomber reports live at [`docs/benchmarks/2026-06-10-template-capacity-za-clean.md`](benchmarks/2026-06-10-template-capacity-za-clean.md) and [`docs/benchmarks/2026-06-10-template-capacity-za-vs.md`](benchmarks/2026-06-10-template-capacity-za-vs.md). p99 collapsed from the stale 1,137/1,319 ms (regression-net co-located CI) to 61/14 ms thanks to the #197 output-caching layer.
- **B6-CLN2 — AOT publish smoke verification.** ✅ **Verified via CI.** `aot-publish-smoke` and `aot-publish-smoke-vs` checks have run green on every PR since [#197](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/197) — both templates AOT-publish cleanly.
- **B6-CLN3 — AGENTS.md refresh.** ✅ **Effectively done.** Both AGENTS.md files were rewritten against the ZA.ORM stack during the swap and subsequent template work. Only a single historical reference remains in za-clean's AGENTS.md ("No EF Core compiled-model dance" — deliberately preserved as a contrast note); no active EF recipes survive.
- **B6-CLN4 — JsonContext SYSLIB1220 / SYSLIB1030 warnings.** ✅ **Resolved.** Fresh Release builds of both templates show zero SYSLIB warnings.

**Diagnosis (the real root cause).** The pre-swap `OrderRepository.cs` in za-clean was the canonical example of the **manual hold-the-slot pattern** the swap was meant to cure: 17 lines of `var openedHere = conn.State != ConnectionState.Open; if (openedHere) await conn.OpenAsync(...); try { ... } finally { if (openedHere) await conn.CloseAsync(); }` surrounding 30 lines of hand-rolled ADO.NET. PR #145's open-model load test quantified the cost of that pattern, but the fix wasn't "tweak the manual code" — it was "stop writing it by hand." ZA.ORM's source generator emits the EF-style ref-counted lifecycle as a side effect of materializing the query, and the entire `openedHere` dance becomes dead code in a generated file.

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
