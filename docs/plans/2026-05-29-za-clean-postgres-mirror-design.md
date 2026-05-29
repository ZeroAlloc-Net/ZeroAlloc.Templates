# za-clean Postgres mirror + cross-template SchemaStrategy sync — design

**Status:** approved 2026-05-29
**Backlog items:** B3 (za-clean replication) + B4 (NBomber-Postgres mirror) — both superseded by this design's unified ship.
**Predecessor PR:** #140 (B2 + NBomber-Postgres on `za-vertical-slice`) — merged.
**Branch:** `feat/za-clean-postgres-mirror`.

## Goal

Replicate PR #140's framework-cost story onto `za-clean`. Both templates end with a unified, AOT-correct schema-management pattern: provider-aware embedded SQL scripts, two-value `Database:SchemaStrategy`, parallel `Migrations.{Sqlite,Postgres}/` folders. The bench-and-NBomber experiment from #140 graduates to za-clean, and `za-vertical-slice` gets pulled up to the same AOT-friendly pattern so the two templates stay structurally in sync.

## Why both at once

PR #140 shipped vertical-slice with reflection-based EF paths at runtime (`MigrateAsync` for Sqlite, `EnsureCreated` for Postgres). That was tolerable for vertical-slice because its `MyApp.csproj` explicitly opts out of AOT publish. But `za-clean` has `<PublishAot>true</PublishAot>` in `MyApp.Api.csproj` — its whole differentiator is AOT correctness. Adopting #140's pattern there would either break AOT or require a per-provider divergence.

The clean answer is to **stop using reflection-based EF runtime paths in either template**. The embedded-script approach `za-clean` already uses for Sqlite generalizes to Postgres via a second `schema.postgres.sql` artifact. `za-vertical-slice` adopts the same pattern (gaining AOT-correctness for free even though its csproj doesn't AOT-publish today). The two templates' DB-config mental models converge.

## Scope locked (Q&A)

- **Q1 — Scope of Postgres support for za-clean?** **B** (chosen): full production AOT + Postgres. Ship `schema.postgres.sql` alongside `schema.sql`; runtime selects via `Database:Provider`.
- **Q2 — Drift management?** **A** (chosen): pull `za-vertical-slice` up to the AOT-friendly pattern in the same PR. Same `SchemaStrategy` enum across both templates; same regen recipe.

## Approach

### Section 1 — Migrations + schema-script layout (both templates)

```
content/<template>/src/<infra-or-myapp>/Persistence/
├── Migrations.Sqlite/      ← renamed from Migrations/, design-time only
│   ├── <timestamp>_InitialCreate.cs
│   ├── <timestamp>_InitialCreate.Designer.cs
│   └── AppDbContextModelSnapshot.cs
├── Migrations.Postgres/    ← NEW, design-time only
│   ├── <timestamp>_InitialCreate.cs
│   ├── <timestamp>_InitialCreate.Designer.cs
│   └── AppDbContextModelSnapshot.cs
├── schema.sql              ← existing (za-clean) / NEW (vertical-slice) — Sqlite DDL, embedded
├── schema.postgres.sql     ← NEW — Npgsql DDL, embedded
└── ...
```

Runtime contract:

| `Database:Provider` | Schema source at runtime |
|---|---|
| `Sqlite` (default) | `ApplyEmbeddedSchemaAsync` reads `schema.sql` |
| `Postgres` | `ApplyEmbeddedSchemaAsync` reads `schema.postgres.sql` |

No `MigrateAsync` / `EnsureCreated` / `EnsureCreatedAsync` calls in either template's production code path. The reflection-based design-time pipeline runs **only** on a contributor's box during `dotnet ef migrations add/script`. AOT publish reads only embedded resources.

**Npgsql AOT compatibility note:** `Npgsql.EntityFrameworkCore.PostgreSQL` and `Npgsql` are `IsTrimmable=true` since 8.x. Adding them to production `MyApp.Api`/`MyApp.Infrastructure` (za-clean) and `MyApp` (vertical-slice) doesn't introduce ILLink warnings. Already verified for vertical-slice in PR #140; same applies to za-clean.

**Adopter regeneration recipe** (after entity changes):

```bash
dotnet ef migrations add <Name> --context AppDbContext --output-dir Persistence/Migrations.Sqlite   -- --provider Sqlite
dotnet ef migrations add <Name> --context AppDbContext --output-dir Persistence/Migrations.Postgres -- --provider Postgres
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.sql           -- --provider Sqlite
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.postgres.sql  -- --provider Postgres
```

A `tools/regen-schema.{sh,ps1}` wrapper bundles the four commands. Both templates ship the wrapper.

### Section 2 — `SchemaStrategy` unification

Both templates collapse to the same 2-value enum:

| Value | Behavior | Used by |
|---|---|---|
| `EmbeddedScript` (default) | `ApplyEmbeddedSchemaAsync` reads the provider-appropriate `schema.*.sql` and applies it. | Production (both providers, both templates), NBomber SUT. |
| `Skip` | Startup does nothing. | `WritePipelineBench`'s `[GlobalSetup]` paths where the bench owns DB lifecycle. |

The `Migrate` and `EnsureCreated` values shipped by PR #140 for vertical-slice **go away**.

### DbContext registration (both templates — different file, same pattern)

```csharp
var provider = config.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = config.GetConnectionString("Default") ?? "Data Source=app.db";

services.AddDbContextPool<AppDbContext>(opts =>
{
    if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        opts.UseNpgsql(connectionString, npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
    else
        opts.UseSqlite(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));

    opts.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
```

- Vertical-slice: lives in `Program.cs`.
- za-clean: lives in `InfrastructureServiceCollectionExtensions.AddMyAppInfrastructure`. The method signature gains a `provider` parameter or reads from passed-in `IConfiguration`.

### DesignTimeDbContextFactory (both templates)

Accept `--provider` arg (or `DOTNET_EF_PROVIDER` env var as fallback):

```csharp
public AppDbContext CreateDbContext(string[] args)
{
    var provider = args.FirstOrDefault(a => a.StartsWith("--provider", StringComparison.Ordinal))
        ?.Split('=', 2).LastOrDefault()
        ?? Environment.GetEnvironmentVariable("DOTNET_EF_PROVIDER")
        ?? "Sqlite";
    var connStr = provider == "Postgres"
        ? "Host=localhost;Database=design;Username=postgres;Password=postgres"
        : "Data Source=design.db";

    var opts = new DbContextOptionsBuilder<AppDbContext>();
    if (provider == "Postgres") opts.UseNpgsql(connStr, npg => npg.MigrationsAssembly(...));
    else opts.UseSqlite(connStr, sql => sql.MigrationsAssembly(...));
    return new AppDbContext(opts.Options);
}
```

### Section 3 — Bench refactor

#### za-clean — `[Params] DbBackend` profile (new)

`content/za-clean/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs` gets the same shape PR #140 added to vertical-slice's bench:

- `[Params(DbBackend.Sqlite, DbBackend.Postgres)]` enum, 1 method × 2 backends → **2 result rows**.
- **Sqlite branch**: in-memory `SqliteConnection`, override `DbContextOptions`, leave `Database:SchemaStrategy` at default (`EmbeddedScript`). WAF startup applies `schema.sql` to the in-memory connection.
- **Postgres branch**: build `bench_<guid8>` DB via `NpgsqlConnectionStringBuilder` + admin connection + `CREATE DATABASE`. Override `DbContextOptions` to `UseNpgsql(workerConn)`. Leave `Database:SchemaStrategy` at default. WAF startup applies `schema.postgres.sql` to the new DB.
- **EF-stack-strip predicate** uses `typeof(AppDbContext)` + namespace prefix match (per the 4e0615f lesson).
- **`BenchShippingClient` stub** for `IShippingQuoteClient`: preserved as-is.
- `[GlobalCleanup]` drops the Postgres DB with `DROP DATABASE IF EXISTS ... WITH (FORCE)` best-effort.

#### Vertical-slice — drop external `EnsureCreated()`

`content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`:

- Remove the post-WAF-build `db.Database.EnsureCreated()` call (currently around lines 162-167).
- Stop setting `Database:SchemaStrategy=Skip` for Postgres. Leave at default (`EmbeddedScript`).
- WAF startup now applies `schema.postgres.sql` via `ApplyEmbeddedSchemaAsync` against the bench's `bench_<guid8>` DB.

#### Shared EF-stack-strip (both benches)

```csharp
var efDescriptors = s
    .Where(d => d.ServiceType == typeof(AppDbContext)
        || (d.ServiceType.FullName is { } n
            && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || n.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal))))
    .ToList();
foreach (var d in efDescriptors) s.Remove(d);
```

The bench's re-registration uses non-pooled `AddDbContext` (not `AddDbContextPool`) — bench owns DbContext lifecycle, pooling complicates per-iteration semantics.

### Section 4 — CI + docs sync

#### CI — `benchmarks.yml`

**BDN matrix (`benchmark` job):** no workflow changes. `za-clean / WritePipeline` matrix leg already exists and the Postgres service container is already job-level (added in PR #140). After bench refactor, this leg produces 2 rows instead of 1.

**NBomber-Postgres:** new sibling job `nbomber-postgres-clean` next to existing `nbomber-postgres-vs`. Mirrors the existing job's structure: explicit `docker run postgres:17 -c max_connections=500` step, SUT-as-background-process with `setsid`, healthz-wait with PID-liveness check, NBomber invocation, cleanup, artifact uploads.

Artifact name pairs:
- `nbomber-za-vertical-slice-postgres` + `nbomber-sut-log-vs` (existing — renamed `nbomber-sut-log` → `nbomber-sut-log-vs`)
- `nbomber-za-clean-postgres` + `nbomber-sut-log-clean` (new)

Trade-off considered: matrixing the two NBomber jobs over `template: [za-clean, za-vertical-slice]`. Rejected — matrixed YAML hides the SUT lifecycle + healthz-wait + log uploads behind `${{ matrix.* }}` substitutions, hurting grep-ability and debuggability for marginal LOC win.

#### Docs sync

- **`docs/za-clean.md`**: append a "Load testing against Postgres" section mirroring `docs/za-vertical-slice.md`'s. Numbers placeholder; post-merge `docs:` commit on `main` fills them in.
- **`docs/za-vertical-slice.md`**:
  - Line 50 bullet update: Sqlite path no longer mentions `MigrateAsync`; both paths now reference `ApplyEmbeddedSchemaAsync`.
  - The `Database:SchemaStrategy=EnsureCreated` mention earlier in the file is removed (value no longer exists).
- **`content/za-clean/README.md`**: existing one-liner "Swap SQLite → PostgreSQL" recipe expands to the multi-line version mirroring vertical-slice's PR #140 form. Mentions `Database:Provider`, `ConnectionStrings:Default`, and the `tools/regen-schema.{sh,ps1}` wrapper.
- **`content/za-vertical-slice/README.md`**: recipe updated — `Database:SchemaStrategy=EnsureCreated` removed; production-Postgres recipe now points at `tools/regen-schema.*`.
- **`docs/backlog.md`**: strike B3 with "shipped 2026-05-29" + cross-template sync note. B4 (vertical-slice replication of NBomber-Postgres) marked superseded by this PR.

#### Adopter-recipe helper

Both templates ship `tools/regen-schema.sh` (or `regen-schema.ps1` for Windows-only adopters):

```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet ef migrations add "${1:-Update}" --context AppDbContext --output-dir Persistence/Migrations.Sqlite   -- --provider Sqlite
dotnet ef migrations add "${1:-Update}" --context AppDbContext --output-dir Persistence/Migrations.Postgres -- --provider Postgres
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.sql           -- --provider Sqlite
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.postgres.sql  -- --provider Postgres
```

PowerShell variant ships the same 4 commands. Both go into `content/<template>/tools/` so the dist matches what adopters get from `dotnet new`.

## Risks / out-of-scope

- **Two-migrations maintenance burden.** Adopters must remember to regenerate both providers after entity changes. Mitigation: the `tools/regen-schema.*` wrapper + a README callout. Acceptable for a template ecosystem.
- **Production Postgres deployment specifics** (connection-string secrets, monitoring, replicas, pooling tuning) stay out-of-scope. The template ships the *capability*; adopter takes it from there.
- **Postgres-18-on-CI** is a separate Renovate PR already in flight (`renovate/postgres-18.x`); we stay on `postgres:17` for B3 to keep diffs focused.
- **AOT-Postgres smoke** — the existing `aot-publish-smoke-clean` workflow currently exercises only Sqlite. Extending it to also smoke Postgres-AOT would catch trim/warn issues earlier but adds CI time. Deferred to a follow-up backlog item if Npgsql ever ships an AOT-incompatible change.

## Graduation

B3 closes when:
1. PR with both templates' refactor + CI + docs merges to `main`.
2. Manual `Benchmarks (manual)` workflow run on `main` produces:
   - 8 BDN rows on `bdn-za-vertical-slice-WritePipeline` (3 methods × 2 backends, no regression).
   - 2 BDN rows on `bdn-za-clean-WritePipeline` (1 method × 2 backends, new).
   - Real numbers in `nbomber-za-vertical-slice-postgres` (preserves PR #140's ~2,540 RPS ceiling).
   - Real numbers in `nbomber-za-clean-postgres` (new).
3. Post-merge `docs:` commits paste the actual numbers into `docs/za-clean.md`'s placeholder and refresh `docs/za-vertical-slice.md`'s NBomber line if it drifts.
