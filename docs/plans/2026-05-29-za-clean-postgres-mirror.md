# za-clean Postgres Mirror + Cross-Template Sync Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replicate PR #140's Postgres + NBomber work onto `za-clean`, AND pull `za-vertical-slice` up to the same AOT-friendly embedded-script schema pattern so the two templates' DB-config mental models converge.

**Architecture:** Both templates ship `schema.sql` (Sqlite DDL) + `schema.postgres.sql` (Npgsql DDL) as embedded resources. `ApplyEmbeddedSchemaAsync` selects the right one at runtime based on `Database:Provider`. Zero EF reflection at runtime in either template's production code path — AOT-correct for za-clean, AOT-ready for vertical-slice when it later opts in. `Database:SchemaStrategy` collapses to a 2-value enum (`EmbeddedScript` / `Skip`); the PR #140-era `Migrate` / `EnsureCreated` values disappear. Parallel `Migrations.{Sqlite,Postgres}/` folders per template enable design-time scaffolding for both providers.

**Tech Stack:** .NET 10, EF Core 10, `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2` (already in CPM from B2), Postgres 17 (already in `benchmarks.yml`'s docker-run pattern).

**Design doc:** `docs/plans/2026-05-29-za-clean-postgres-mirror-design.md` (committed `2744860`).
**Branch:** `feat/za-clean-postgres-mirror` (off latest main, PR #140 already merged).

---

## Conventions

- All paths relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.
- Local builds are blocked by the standing SDK pin (`global.json` requires `10.0.300`; installed `10.0.204`). CI is the source of truth for all verifications.
- Each task ends with a commit using conventional-commit prefixes. Use the repo's `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer via HEREDOC.
- **Atomicity constraints** are called out per-task — tasks marked ATOMIC must land their listed files together in one commit.
- **EF migration scaffolding tasks** (Tasks 3 and 9) require running `dotnet ef ...` commands that need the SDK. If the standing SDK pin blocks these locally, defer to the implementer subagent — it may need to either (a) install SDK 10.0.300, (b) temporarily rename global.json (with user consent), or (c) flag the blockage and ask the controller for guidance. CI cannot run `dotnet ef migrations add` — the artifacts must be checked in.

---

## Task 1: Add Npgsql to za-clean's production Infrastructure csproj

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj` — add `<PackageReference>` in the EF Core block.

**Step 1:** In the EF Core block (currently lines 7-11), insert after the `Microsoft.EntityFrameworkCore.Sqlite` line:

```xml
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
```

Version is already centrally managed at `10.0.2` in `Directory.Packages.props`. No `Version=` attribute.

**Step 2:** Commit.

```powershell
git add content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj
git commit -m "chore(deps): add Npgsql to za-clean MyApp.Infrastructure.csproj"
```

Body explains: za-clean's production Infrastructure layer gains the Npgsql EF provider so `AddMyAppInfrastructure` can register `UseNpgsql` when `Database:Provider=Postgres`. Sqlite default path preserved; ~2 MB additional disk footprint only loaded when Postgres is selected. AOT correctness preserved — Npgsql is `IsTrimmable=true` since 8.x.

---

## Task 2: Add Database:Provider switch to za-clean's Infrastructure layer

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- Modify: `content/za-clean/src/MyApp.Api/Program.cs` (caller update)

**Step 1:** Update `AddMyAppInfrastructure` signature in `InfrastructureServiceCollectionExtensions.cs` to accept a `provider` string parameter (Sqlite|Postgres). The existing signature is:

```csharp
public static IServiceCollection AddMyAppInfrastructure(
    this IServiceCollection services,
    string sqliteConnectionString,   // rename to `connectionString`
    string shippingBaseUrl)
```

Becomes:

```csharp
public static IServiceCollection AddMyAppInfrastructure(
    this IServiceCollection services,
    string provider,
    string connectionString,
    string shippingBaseUrl)
```

**Step 2:** Replace the `services.AddDbContext<AppDbContext>` block (currently lines 27-41) with the provider-aware pooled registration:

```csharp
services.AddDbContextPool<AppDbContext>(opts =>
{
    if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        opts.UseNpgsql(connectionString, npg =>
            npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
    }
    else
    {
        opts.UseSqlite(connectionString, sql =>
            sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
    }
    // Compiled model required for AOT publish; bypasses the reflection-based
    // design-time model pipeline. Regenerate via
    // `dotnet ef dbcontext optimize --output-dir Persistence/CompiledModel`
    // after any entity/mapping change.
    opts.UseModel(AppDbContextModel.Instance);
    // Owned-type snapshot diff produces a spurious "pending changes" warning on EF 9
    // against the existing InitialCreate migration. Tolerated for the template; a real
    // app should regenerate the migration when the snapshot legitimately drifts.
    opts.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
```

Note: `AddDbContextPool` replaces `AddDbContext` (per PR #140's perf experiment finding — ~13% RPS gain at NBomber-scale concurrency). `AppDbContext`'s single-arg constructor at `Persistence/AppDbContext.cs:17` is pool-compatible.

**Step 3:** Update `Program.cs` caller. The existing call at line 27 is:

```csharp
builder.Services.AddMyAppInfrastructure(connectionString, shippingBaseUrl);
```

Change to:

```csharp
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
builder.Services.AddMyAppInfrastructure(dbProvider, connectionString, shippingBaseUrl);
```

Read `dbProvider` from config right before the `AddMyAppInfrastructure` call, so it's adjacent in the file.

**Step 4:** Commit.

```powershell
git add content/za-clean/src/MyApp.Infrastructure/InfrastructureServiceCollectionExtensions.cs content/za-clean/src/MyApp.Api/Program.cs
git commit -m "feat(za-clean): explicit Database:Provider config (Sqlite|Postgres)"
```

Body: za-clean's Infrastructure layer now dispatches between UseSqlite and UseNpgsql based on a `Database:Provider` config key. AddDbContext → AddDbContextPool (PR #140's perf optimization). Default Sqlite preserves zero-setup quickstart. Production Postgres adopters set `Database:Provider=Postgres` + `ConnectionStrings:Default=Host=...`.

---

## Task 3: Rename Migrations/ → Migrations.Sqlite/ + scaffold Postgres migrations

**Why this exists:** EF migrations are provider-specific. The existing migration declares Sqlite-typed columns (`INTEGER`, `TEXT`). For provider-agnostic schema management we need two parallel migration histories: one for each provider. This also unlocks AOT-correct Postgres schema deployment via `dotnet ef migrations script -- --provider Postgres`.

**Atomicity:** This task touches the EF migrations directory structure. It MUST land before Task 4 (which generates `schema.postgres.sql` from those new migrations).

**Files:**
- Rename: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/` → `Persistence/Migrations.Sqlite/`
- Create: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Postgres/*` (scaffolded by `dotnet ef`)
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` (provider-aware so `dotnet ef migrations add ... -- --provider Postgres` scaffolds Postgres-typed migrations)

**Step 1:** Update `DesignTimeDbContextFactory.cs` to accept `--provider` arg:

```csharp
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyApp.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by the EF Core tooling (`dotnet ef migrations add`,
/// `dotnet ef migrations script`) to construct an <see cref="AppDbContext"/>
/// without running the host's DI container.
///
/// Accepts a `--provider Sqlite|Postgres` argument (passed after `--` on the
/// `dotnet ef` command line) so the same factory scaffolds both migration
/// histories. Falls back to the `DOTNET_EF_PROVIDER` env var, then to Sqlite.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = args.FirstOrDefault(a => a.StartsWith("--provider", StringComparison.Ordinal))
            ?.Split('=', 2).Skip(1).FirstOrDefault()
            ?? args.SkipWhile(a => a != "--provider").Skip(1).FirstOrDefault()
            ?? Environment.GetEnvironmentVariable("DOTNET_EF_PROVIDER")
            ?? "Sqlite";

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql(
                "Host=localhost;Database=design;Username=postgres;Password=postgres",
                npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
        }
        else
        {
            builder.UseSqlite(
                "Data Source=design.db",
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
        }

        return new AppDbContext(builder.Options);
    }
}
```

Note: handles both `--provider Postgres` (space-separated) and `--provider=Postgres` (equals-separated) forms.

**Step 2:** Rename the migrations folder:

```powershell
git mv content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Sqlite
```

**Step 3:** Update the `MigrationsAssembly` references — actually unnecessary at this step; EF discovers migrations by inspecting the assembly's `[Migration]`-attributed classes, not by folder name. The folder name is conventional only. So renaming the folder is sufficient.

**Step 4:** Scaffold the Postgres migration. From `content/za-clean/src/MyApp.Infrastructure/`:

```powershell
dotnet ef migrations add InitialCreate --context AppDbContext --output-dir Persistence/Migrations.Postgres -- --provider Postgres
```

Expected: creates `Persistence/Migrations.Postgres/<timestamp>_InitialCreate.cs` + `.Designer.cs` + `AppDbContextModelSnapshot.cs` with Postgres-typed columns (e.g., `integer`, `text`, `numeric` instead of Sqlite's `INTEGER`/`TEXT`).

**Caveat:** the `dotnet ef migrations add` command needs the SDK to run. If blocked by the standing SDK pin (10.0.300 vs installed 10.0.204), the implementer should either install 10.0.300 OR ask the controller for permission to temporarily rename `global.json` (per the recipe used during PR #140's local-NBomber experiment).

**Step 5:** EF will create the Postgres migration WITH ITS OWN `AppDbContextModelSnapshot.cs`. The snapshot file will conflict if both folders are on the same `MigrationsAssembly`. EF handles this via the `[DbContext]` attribute on snapshot classes — they're per-provider, so EF picks the snapshot matching the active provider at design time. **Verify** the generated `Migrations.Postgres/AppDbContextModelSnapshot.cs` does not name-collide with `Migrations.Sqlite/AppDbContextModelSnapshot.cs` (both are the same class name in the same namespace, which IS a compile error).

If they DO collide (likely on EF 10's default scaffolding), the fix is to manually edit one snapshot's namespace, e.g. add a sub-namespace `MyApp.Infrastructure.Persistence.Migrations.Postgres` (rename the namespace in the file). The same fix applies to `AppDbContextModelSnapshot.cs` and to the `[DbContext]` attribute referencing it. Document this in the commit message.

**Step 6:** Commit.

```powershell
git add content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Sqlite content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Postgres content/za-clean/src/MyApp.Infrastructure/Persistence/DesignTimeDbContextFactory.cs
git commit -m "feat(za-clean/migrations): parallel Sqlite + Postgres migration folders"
```

Body explains: design-time scaffolding now supports both providers via `dotnet ef ... -- --provider Sqlite|Postgres`. Folder rename keeps existing Sqlite migrations intact. New Postgres migration declares Npgsql-typed columns. Namespace-rename details (if applicable) noted.

---

## Task 4: Generate schema.postgres.sql

**Files:**
- Create: `content/za-clean/src/MyApp.Infrastructure/Persistence/schema.postgres.sql`
- Modify: `content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj` (embed as resource)

**Step 1:** From `content/za-clean/src/MyApp.Infrastructure/`:

```powershell
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.postgres.sql -- --provider Postgres
```

Expected: creates `Persistence/schema.postgres.sql` with Npgsql idempotent DDL (uses `pg_catalog`/`information_schema` for the migration-history check, not `sqlite_master`).

**Step 2:** Verify the file exists and is Postgres-shaped. Open it; the top should reference `pg_catalog.pg_constraint` or similar, not `sqlite_master`.

**Step 3:** Embed as resource. Check if `MyApp.Infrastructure.csproj` already has an `EmbeddedResource` entry for `schema.sql`. If yes, add an analogous entry for `schema.postgres.sql`. If no (i.e., embedding is via the SDK's default `<EmbeddedResource Include="**/schema*.sql" />`-style globbing or via `MyApp.Api.csproj`), trace where the existing `schema.sql` is embedded and add `schema.postgres.sql` there.

Likely location: `content/za-clean/src/MyApp.Api/MyApp.Api.csproj` (since `ApplyEmbeddedSchemaAsync` in `Program.cs` reads from `typeof(Program).Assembly`). Verify by inspecting the csproj.

If the embedding is in Api.csproj, add an `EmbeddedResource` entry there pointing at `..\MyApp.Infrastructure\Persistence\schema.postgres.sql`. Match the existing `schema.sql` entry pattern.

**Step 4:** Commit.

```powershell
git add content/za-clean/src/MyApp.Infrastructure/Persistence/schema.postgres.sql content/za-clean/src/MyApp.Api/MyApp.Api.csproj   # or wherever the embed is
git commit -m "feat(za-clean): embed schema.postgres.sql for Npgsql startup path"
```

Body: Generated via `dotnet ef migrations script ... -- --provider Postgres` from the Postgres-typed migrations added in the previous commit. Embedded as a resource alongside the existing Sqlite `schema.sql` so the AOT-published binary can apply either schema based on `Database:Provider`.

---

## Task 5 (ATOMIC): Provider-aware ApplyEmbeddedSchemaAsync + SchemaStrategy in za-clean

**Atomicity:** This task touches Program.cs's startup schema-creation block. The change MUST land together because Task 6 (bench refactor) depends on the new schema strategy being honored at runtime.

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/Program.cs` (`ApplyEmbeddedSchemaAsync` function, around lines 117-180)

**Step 1:** Wrap the existing startup-migration block in a `Database:SchemaStrategy` check. Current code (lines 117-137):

```csharp
// Ensure the database schema exists on startup so a fresh dev box doesn't
// need a separate `dotnet ef database update` step. ...
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplyEmbeddedSchemaAsync(db);
    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(db);
    }
}
```

Replace with:

```csharp
// Apply schema on startup. `Database:SchemaStrategy` controls how:
//
//   EmbeddedScript  (default) — load schema.sql (Sqlite) or schema.postgres.sql
//                   (Postgres) from embedded resources and apply via raw ADO.NET.
//                   AOT-compatible — no reflection.
//   Skip            — startup does nothing. Used by WritePipelineBench's
//                   [GlobalSetup] paths where the bench owns DB lifecycle.
var schemaStrategy = builder.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplyEmbeddedSchemaAsync(db, dbProvider);
    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(db);
    }
}
```

(`dbProvider` is the same variable Task 2 added earlier in Program.cs — it's in scope here because top-level statements share scope.)

**Step 2:** Update `ApplyEmbeddedSchemaAsync` signature to accept the provider and pick the resource name + idempotency check accordingly:

```csharp
static async Task ApplyEmbeddedSchemaAsync(AppDbContext db, string provider)
{
    var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);
    var resourceSuffix = isPostgres ? "schema.postgres.sql" : "schema.sql";

    var asm = typeof(Program).Assembly;
    var resourceName = asm.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
    using var stream = asm.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream);
    var script = await reader.ReadToEndAsync();

    var conn = db.Database.GetDbConnection();
    var openedHere = conn.State != System.Data.ConnectionState.Open;
    if (openedHere)
    {
        await conn.OpenAsync();
    }
    try
    {
        // Idempotency check — provider-specific.
        await using (var check = conn.CreateCommand())
        {
            check.CommandText = isPostgres
                ? "SELECT to_regclass('public.\"__EFMigrationsHistory\"');"  // returns NULL if missing
                : "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var exists = await check.ExecuteScalarAsync();
            // Sqlite returns the table name or null; Postgres returns the regclass oid or DBNull.
            var hasHistory = exists is not null && exists is not DBNull;
            if (hasHistory)
            {
                return;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = script;
        await cmd.ExecuteNonQueryAsync();
    }
    finally
    {
        if (openedHere)
        {
            await conn.CloseAsync();
        }
    }
}
```

Key behavior changes:
- Picks `schema.sql` vs `schema.postgres.sql` from embedded resources.
- Provider-specific idempotency check (`sqlite_master` for Sqlite, `to_regclass` for Postgres).
- `to_regclass('public."__EFMigrationsHistory"')` returns `NULL` (which surfaces as `DBNull`) if the table doesn't exist. The combined `exists is not null && exists is not DBNull` handles both null forms.

**Step 3:** Commit.

```powershell
git add content/za-clean/src/MyApp.Api/Program.cs
git commit -m "feat(za-clean): provider-aware ApplyEmbeddedSchemaAsync + SchemaStrategy"
```

Body: `Database:SchemaStrategy` config (EmbeddedScript|Skip, defaults to EmbeddedScript) gates the startup schema-apply. `ApplyEmbeddedSchemaAsync` now selects schema.sql (Sqlite) or schema.postgres.sql (Postgres) based on `Database:Provider` and runs the provider-appropriate idempotency check (sqlite_master vs to_regclass). Zero new reflection; AOT publish unaffected.

---

## Task 6 (ATOMIC with Task 5): za-clean WritePipelineBench [Params] DbBackend refactor

**Files:**
- Modify: `content/za-clean/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`
- Modify: `content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj` (add Npgsql PackageReference)

**Step 1:** Add Npgsql to MyApp.Benchmarks.csproj if not already present. Verify by inspecting the csproj; if missing, add `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />` in the package-ref block.

**Step 2:** Rewrite the bench file. Whole-file structure mirrors `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs` (from PR #140 final state at `4e0615f`), adapted for za-clean's 1-method bench and Clean Architecture namespacing.

Key changes vs current state:
- Add `using Npgsql;` and `using NpgsqlConnectionStringBuilder = Npgsql.NpgsqlConnectionStringBuilder;` (or fully-qualified — match the za-vertical-slice idiom).
- Add `public enum DbBackend { Sqlite, Postgres }` at the bottom of the file (outside the class, inside the namespace).
- Add `[Params(DbBackend.Sqlite, DbBackend.Postgres)] public DbBackend Backend { get; set; }` property.
- Add private fields: `_postgresAdminConnString` (string?), `_postgresDbName` (string?).
- Refactor `[GlobalSetup]` to branch on `Backend`. Sqlite branch unchanged. Postgres branch creates `bench_<guid8>` via `NpgsqlConnectionStringBuilder` + admin connection + `CREATE DATABASE`.
- Refactor `ConfigureServices` to strip all `Microsoft.EntityFrameworkCore.*` / `Npgsql.EntityFrameworkCore.*` descriptors AND `typeof(AppDbContext)` (per the 4e0615f lesson). Re-add via plain `AddDbContext` (not pool).
- WAF config sets `["Database:Provider"]` to Sqlite or Postgres AND `["Database:SchemaStrategy"]` to default ("EmbeddedScript") — so Program.cs's startup hook applies the right script.
- Postgres branch does NOT call `EnsureCreated()` externally — WAF startup handles it via the new `ApplyEmbeddedSchemaAsync(db, "Postgres")` path.
- `[GlobalCleanup]` drops the Postgres DB with `DROP DATABASE IF EXISTS ... WITH (FORCE)`.
- Existing `IShippingQuoteClient`/`BenchShippingClient` stub preserved.

Full snippet (paste into the file, replacing the entire current content):

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using Npgsql;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks;

/// <summary>
/// Single-method WritePipeline benchmark for the za-clean Clean Architecture
/// template. Hosts the API via WebApplicationFactory&lt;Program&gt; and runs
/// POST /orders end-to-end through ASP.NET middleware, mediator dispatch,
/// validation, EF Core SaveChanges, and a stubbed shipping client.
///
/// <para>
/// <b>Backends:</b> <c>[Params]</c> dispatches the benchmark against both
/// in-memory SQLite and a localhost Postgres. Sqlite uses the production
/// schema path (<c>Program.cs</c>'s <c>ApplyEmbeddedSchemaAsync</c> reading
/// <c>schema.sql</c>); Postgres creates a fresh per-process database
/// (<c>bench_&lt;guid8&gt;</c>) and applies <c>schema.postgres.sql</c> via
/// the same path. Both code paths are AOT-correct (no EF reflection at runtime).
/// </para>
///
/// <para>
/// <b>Local dev — Postgres profile only:</b>
/// <code>
/// docker run --rm -d -p 5432:5432 \
///   -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
///   --name bench-pg postgres:17 \
///   -c max_connections=500
/// </code>
/// then <c>dotnet run -c Release -- --filter "*WritePipelineBench*"</c>.
/// CI provisions Postgres via the <c>services:</c> block in
/// <c>.github/workflows/benchmarks.yml</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class WritePipelineBench
{
    [Params(DbBackend.Sqlite, DbBackend.Postgres)]
    public DbBackend Backend { get; set; }

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private SqliteConnection? _connection;
    private string? _postgresAdminConnString;
    private string? _postgresDbName;
    private object? _request;

    [GlobalSetup]
    public void Setup()
    {
        NpgsqlConnectionStringBuilder? csb = null;

        if (Backend == DbBackend.Sqlite)
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }
        else
        {
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
            var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
            var pwd = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
            var adminDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bench";

            csb = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.Parse(port, System.Globalization.CultureInfo.InvariantCulture),
                Username = user,
                Password = pwd,
                Database = adminDb,
            };
            _postgresAdminConnString = csb.ConnectionString;
            _postgresDbName = "bench_" + Guid.NewGuid().ToString("N")[..8];

            using var admin = new NpgsqlConnection(_postgresAdminConnString);
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_postgresDbName}\"";
            cmd.ExecuteNonQuery();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:DevSigningKey"] = TestJwt.DevKey,
                ["Database:Provider"] = Backend == DbBackend.Postgres ? "Postgres" : "Sqlite",
                ["Database:SchemaStrategy"] = "EmbeddedScript",
            }));
            b.ConfigureServices(s =>
            {
                // Strip all EF Core registrations the production AddDbContextPool
                // left behind. typeof(AppDbContext) catches the pool-flavored
                // factory descriptor (lives in MyApp.Infrastructure.Persistence
                // namespace, not Microsoft.EntityFrameworkCore.*).
                var efDescriptors = s
                    .Where(d => d.ServiceType == typeof(AppDbContext)
                        || (d.ServiceType.FullName is { } n
                            && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                                || n.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal))))
                    .ToList();
                foreach (var d in efDescriptors)
                {
                    s.Remove(d);
                }

                if (Backend == DbBackend.Sqlite)
                {
                    s.AddDbContext<AppDbContext>(opt =>
                    {
                        opt.UseSqlite(_connection!, sqlite =>
                            sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                        opt.ConfigureWarnings(w => w.Ignore(
                            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    });
                }
                else
                {
                    csb!.Database = _postgresDbName;
                    var workerConnString = csb.ConnectionString;
                    s.AddDbContext<AppDbContext>(opt =>
                    {
                        opt.UseNpgsql(workerConnString);
                        opt.ConfigureWarnings(w => w.Ignore(
                            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    });
                }

                var shippingDescriptor = s.SingleOrDefault(d => d.ServiceType == typeof(IShippingQuoteClient));
                if (shippingDescriptor is not null)
                {
                    s.Remove(shippingDescriptor);
                }
                s.AddScoped<IShippingQuoteClient, BenchShippingClient>();
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["orders.write"]));

        _request = new
        {
            customerId = 42,
            items = new[] { new { sku = "SKU-1", quantity = 2, unitPriceEur = 15m } },
            shippingZip = "1011AA",
        };
    }

    [Benchmark]
    public async Task<HttpResponseMessage> WritePipeline()
        => await _client!.PostAsJsonAsync("/orders", _request);

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _connection?.Dispose();

        if (Backend == DbBackend.Postgres && _postgresAdminConnString is not null && _postgresDbName is not null)
        {
            try
            {
                using var admin = new NpgsqlConnection(_postgresAdminConnString);
                admin.Open();
                using var cmd = admin.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_postgresDbName}\" WITH (FORCE)";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Best-effort cleanup. CI containers are ephemeral; local devs
                // can drop stale bench_* databases manually if a process crashes.
            }
        }
    }
}

internal sealed class BenchShippingClient : IShippingQuoteClient
{
    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        => Task.FromResult(Money.TryCreate(5m, "EUR"));
}

public enum DbBackend
{
    Sqlite,
    Postgres,
}
```

**Step 3:** Commit (atomic with Task 5).

```powershell
git add content/za-clean/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj content/za-clean/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs
git commit -m "feat(za-clean/bench): add Postgres profile via [Params] DbBackend"
```

Body: Cross-products WritePipeline × DbBackend (Sqlite + Postgres) → 2 result rows per artifact. Postgres branch creates fresh bench_<guid8> via NpgsqlConnectionStringBuilder and lets WAF startup apply schema.postgres.sql via the new provider-aware ApplyEmbeddedSchemaAsync path. EF-stack-strip with typeof(AppDbContext) handles the AddDbContextPool flavor. Shipping client stub preserved.

**Note on Task 5 + 6 atomicity:** The two tasks should ideally land as a single commit since the bench depends on Program.cs honoring `Database:SchemaStrategy` and selecting the right schema script. In practice, splitting them across commits 5 and 6 is acceptable IF commit 5 is correct in isolation (the Sqlite path still works via the existing default behavior) and commit 6 is the first time the Postgres path is exercised. The implementer subagent may choose to bundle them into one commit if it feels cleaner — that's fine.

---

## Task 7: nbomber-postgres-clean CI job + artifact rename

**Files:**
- Modify: `.github/workflows/benchmarks.yml`

**Step 1:** Rename the existing `nbomber-postgres-vs` job's `nbomber-sut-log` artifact to `nbomber-sut-log-vs` so the new job's `nbomber-sut-log-clean` artifact doesn't collide. Find the existing job's `Upload SUT log` step (around line 200) and change:

```yaml
          name: nbomber-sut-log
```

to:

```yaml
          name: nbomber-sut-log-vs
```

**Step 2:** Append the new `nbomber-postgres-clean` job at the bottom of the file. Mirror `nbomber-postgres-vs`'s structure but adjust paths for za-clean:

```yaml

  nbomber-postgres-clean:
    name: za-clean / NBomber-Postgres
    runs-on: ubuntu-latest
    timeout-minutes: 30
    env:
      POSTGRES_DB_NAME: myapp_load
    # Postgres runs as an explicit docker-run step (not a GHA service container)
    # so we can pass `-c max_connections=500` to the postgres binary itself.
    # Same pattern as nbomber-postgres-vs — see that job for the rationale.
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Start Postgres
        run: |
          docker run --rm -d --name postgres \
            -p 5432:5432 \
            -e POSTGRES_PASSWORD=postgres \
            -e POSTGRES_DB=${{ env.POSTGRES_DB_NAME }} \
            postgres:17 \
            -c max_connections=500
          for i in {1..30}; do
            if docker exec postgres pg_isready -U postgres > /dev/null 2>&1; then
              echo "Postgres ready after ${i}s"
              exit 0
            fi
            sleep 1
          done
          echo "Postgres did not become ready in 30s"
          docker logs postgres
          exit 1

      - name: Build SUT
        working-directory: content/za-clean
        run: dotnet build src/MyApp.Api -c Release

      - name: Build LoadTest
        working-directory: content/za-clean
        run: dotnet build benchmarks/MyApp.LoadTest -c Release

      - name: Start SUT against Postgres
        working-directory: content/za-clean
        env:
          Database__Provider: Postgres
          Database__SchemaStrategy: EmbeddedScript
          ConnectionStrings__Default: "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=${{ env.POSTGRES_DB_NAME }};Maximum Pool Size=500"
          ASPNETCORE_URLS: "http://localhost:5000"
        run: |
          setsid nohup dotnet run -c Release --no-build --project src/MyApp.Api > sut.log 2>&1 &
          echo $! > sut.pid

      - name: Wait for /healthz
        run: |
          for i in {1..60}; do
            if curl -fsS http://localhost:5000/healthz > /dev/null; then
              echo "SUT healthy after ${i}s"
              exit 0
            fi
            if ! kill -0 "$(cat content/za-clean/sut.pid)" 2>/dev/null; then
              echo "SUT process died before becoming healthy — dumping log"
              cat content/za-clean/sut.log
              exit 1
            fi
            sleep 1
          done
          echo "SUT did not become healthy in 60s — dumping log"
          cat content/za-clean/sut.log
          exit 1

      - name: Run NBomber
        working-directory: content/za-clean
        run: dotnet run -c Release --no-build --project benchmarks/MyApp.LoadTest -- http://localhost:5000

      - name: Stop SUT
        if: always()
        working-directory: content/za-clean
        run: |
          if [ -f sut.pid ]; then
            kill -TERM -"$(cat sut.pid)" || true
          fi

      - name: Stop Postgres
        if: always()
        run: docker stop postgres || true

      - name: Upload NBomber reports
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: nbomber-za-clean-postgres
          path: content/za-clean/reports/**
          if-no-files-found: warn
          retention-days: 30

      - name: Upload SUT log
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: nbomber-sut-log-clean
          path: content/za-clean/sut.log
          if-no-files-found: warn
          retention-days: 7
```

**Step 3:** Update the file-header comment block to mention the new job. Find the "Two jobs:" paragraph in the existing header (after PR #140 it lists `benchmark` + `nbomber-postgres-vs`). Update to list three jobs:

```yaml
# Three jobs:
#   - benchmark (matrix): BDN Primitives + WritePipeline for both templates.
#     The za-vertical-slice/WritePipeline leg cross-products SQLite + Postgres
#     via WritePipelineBench's [Params] DbBackend (3 methods × 2 = 6 rows).
#     The za-clean/WritePipeline leg does the same with 1 method × 2 = 2 rows.
#   - nbomber-postgres-vs: za-vertical-slice load test against real Postgres.
#   - nbomber-postgres-clean: za-clean load test against real Postgres.
```

**Step 4:** Commit.

```powershell
git add .github/workflows/benchmarks.yml
git commit -m "ci(nbomber): add nbomber-postgres-clean + rename vs artifacts"
```

Body: Sibling job to nbomber-postgres-vs running NBomber against za-clean's SUT on real Postgres. Existing nbomber-postgres-vs's SUT log artifact renamed from `nbomber-sut-log` to `nbomber-sut-log-vs` to disambiguate from the new `nbomber-sut-log-clean`. Same docker-run + setsid + healthz-wait pattern as nbomber-postgres-vs.

---

## Task 8: za-clean tools/regen-schema scripts + README + docs

**Files:**
- Create: `content/za-clean/tools/regen-schema.sh`
- Create: `content/za-clean/tools/regen-schema.ps1`
- Modify: `content/za-clean/README.md` (the "Swap SQLite → PostgreSQL" recipe, around line 90)
- Modify: `docs/za-clean.md` (append "Load testing against Postgres" section at the end)

**Step 1:** Create `content/za-clean/tools/regen-schema.sh`:

```bash
#!/usr/bin/env bash
# Regenerate both providers' migration histories + embedded schema scripts
# after entity changes. Run from the Infrastructure csproj directory.
#
# Usage: ./tools/regen-schema.sh [MigrationName]
#   MigrationName defaults to "Update".
set -euo pipefail
NAME="${1:-Update}"

cd "$(dirname "$0")/../src/MyApp.Infrastructure"

dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Sqlite   -- --provider Sqlite
dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Postgres -- --provider Postgres
dotnet ef migrations script --context AppDbContext --idempotent \
  --output Persistence/schema.sql           -- --provider Sqlite
dotnet ef migrations script --context AppDbContext --idempotent \
  --output Persistence/schema.postgres.sql  -- --provider Postgres

echo "Regenerated migrations + schema scripts for both providers."
```

Set executable bit (`chmod +x`) — git will track it via `core.fileMode`.

**Step 2:** Create `content/za-clean/tools/regen-schema.ps1` (PowerShell variant):

```powershell
# Regenerate both providers' migration histories + embedded schema scripts
# after entity changes. Run from the za-clean root.
#
# Usage: .\tools\regen-schema.ps1 [-Name MigrationName]
#   -Name defaults to "Update".
param([string]$Name = "Update")

$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot/../src/MyApp.Infrastructure"
try {
    dotnet ef migrations add $Name --context AppDbContext `
      --output-dir Persistence/Migrations.Sqlite   -- --provider Sqlite
    dotnet ef migrations add $Name --context AppDbContext `
      --output-dir Persistence/Migrations.Postgres -- --provider Postgres
    dotnet ef migrations script --context AppDbContext --idempotent `
      --output Persistence/schema.sql           -- --provider Sqlite
    dotnet ef migrations script --context AppDbContext --idempotent `
      --output Persistence/schema.postgres.sql  -- --provider Postgres

    Write-Host "Regenerated migrations + schema scripts for both providers."
} finally {
    Pop-Location
}
```

**Step 3:** Update `content/za-clean/README.md`'s "Swap SQLite → PostgreSQL" line (around line 90). Current text:

```markdown
- **Swap SQLite → PostgreSQL**: change `UseSqlite` to `UseNpgsql` in `Program.cs`, add the EF provider, regenerate migrations. See the template docs for the recipe.
```

Replace with the multi-line recipe (model on `content/za-vertical-slice/README.md:137-141`):

```markdown
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. AOT-correct: production startup applies the embedded `schema.postgres.sql` via `ApplyEmbeddedSchemaAsync` — no EF reflection at runtime. For load-testing or ad-hoc experimentation, set `Database:SchemaStrategy=EmbeddedScript` (the default). After entity changes, regenerate both providers' migrations + schema scripts:
  ```bash
  tools/regen-schema.sh              # bash
  pwsh tools/regen-schema.ps1        # PowerShell
  ```
  This produces fresh `Migrations.Sqlite/`, `Migrations.Postgres/`, `schema.sql`, and `schema.postgres.sql` so both providers stay in lockstep.
```

**Step 4:** Append a "Load testing against Postgres" section to `docs/za-clean.md`. Use the same structure as `docs/za-vertical-slice.md`'s section (created in PR #140). Numbers section is a placeholder for the post-merge fill-in:

```markdown

## Load testing against Postgres

NBomber's `MyApp.LoadTest` previously targeted in-memory SQLite via the production app — capped at ~470 RPS by SQLite's single-process file lock. That ceiling is the lock, not the framework. Running against Postgres reveals the real throughput.

The SUT and NBomber run as separate processes. The SUT is configured for Postgres via env vars; NBomber's scenario code is unchanged.

### Local recipe

```bash
# 1. Start Postgres
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
  --name myapp-load-pg postgres:17 \
  -c max_connections=500

# 2. Start the SUT
Database__Provider=Postgres \
Database__SchemaStrategy=EmbeddedScript \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
dotnet run -c Release --project src/MyApp.Api &

# 3. Wait for /healthz, then run NBomber
until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
dotnet run -c Release --project benchmarks/MyApp.LoadTest

# Cleanup
kill %1; docker stop myapp-load-pg
```

The startup `ApplyEmbeddedSchemaAsync` step picks `schema.postgres.sql` (embedded resource) and applies it to the fresh `myapp_load` database. AOT-published binaries follow the same code path — zero EF reflection at runtime.

### CI

The `nbomber-postgres-clean` job in `.github/workflows/benchmarks.yml` runs the recipe above end-to-end on every manual workflow trigger. Artifacts:

- `nbomber-za-clean-postgres` — NBomber's HTML / CSV / Markdown reports.
- `nbomber-sut-log-clean` — the SUT's stdout/stderr (kept short, 7-day retention).

### Numbers

> Filled in after the first post-merge CI run on `main`. Same flow as the za-vertical-slice numbers (PR #140 lands the harness; a follow-up commit on `main` pastes the numbers).
```

**Step 5:** Commit.

```powershell
git add content/za-clean/tools content/za-clean/README.md docs/za-clean.md
git commit -m "docs(za-clean): regen-schema scripts + README recipe + load-test section"
```

Body: Bundles three discoverability updates for adopters. `tools/regen-schema.{sh,ps1}` wraps the 4-line migrate + script regeneration; README's "Swap SQLite → PostgreSQL" expands from a one-liner to a real recipe with the AOT-correctness callout; `docs/za-clean.md` gains a "Load testing against Postgres" section mirroring `docs/za-vertical-slice.md`'s. Numbers section a placeholder for post-merge fill-in.

---

## Task 9 (ATOMIC): za-vertical-slice — schema.sql + schema.postgres.sql + Migrations.Sqlite rename + Migrations.Postgres scaffold

**Files:**
- Rename: `content/za-vertical-slice/src/MyApp/Persistence/Migrations/` → `Persistence/Migrations.Sqlite/`
- Create: `content/za-vertical-slice/src/MyApp/Persistence/Migrations.Postgres/*`
- Create: `content/za-vertical-slice/src/MyApp/Persistence/schema.sql` (NEW for vs — embedded resource)
- Create: `content/za-vertical-slice/src/MyApp/Persistence/schema.postgres.sql`
- Modify: `content/za-vertical-slice/src/MyApp/Persistence/DesignTimeDbContextFactory.cs` (if exists; create if not)
- Modify: `content/za-vertical-slice/src/MyApp/MyApp.csproj` (embed both schema files as resources)

**Step 1:** Check if `content/za-vertical-slice/src/MyApp/Persistence/DesignTimeDbContextFactory.cs` exists. If yes, update it analogously to za-clean's Task 3. If no, create it — same content as za-clean's factory but in the `MyApp.Persistence` namespace.

**Step 2:** Rename:

```powershell
git mv content/za-vertical-slice/src/MyApp/Persistence/Migrations content/za-vertical-slice/src/MyApp/Persistence/Migrations.Sqlite
```

**Step 3:** Scaffold the Postgres migration. From `content/za-vertical-slice/src/MyApp/`:

```powershell
dotnet ef migrations add InitialCreate --context AppDbContext --output-dir Persistence/Migrations.Postgres -- --provider Postgres
```

Resolve any snapshot-class collision the same way as za-clean's Task 3 Step 5.

**Step 4:** Generate both schema scripts. From `content/za-vertical-slice/src/MyApp/`:

```powershell
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.sql           -- --provider Sqlite
dotnet ef migrations script --context AppDbContext --idempotent --output Persistence/schema.postgres.sql  -- --provider Postgres
```

**Step 5:** Embed both scripts as resources in `MyApp.csproj`. Find the appropriate ItemGroup (or add a new one) and include:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Persistence\schema.sql" />
    <EmbeddedResource Include="Persistence\schema.postgres.sql" />
  </ItemGroup>
```

**Step 6:** Commit.

```powershell
git add content/za-vertical-slice/src/MyApp/Persistence content/za-vertical-slice/src/MyApp/MyApp.csproj
git commit -m "feat(za-vertical-slice): parallel migrations + embedded schema scripts"
```

Body: Rename `Migrations/` → `Migrations.Sqlite/`. Add `Migrations.Postgres/` via `dotnet ef migrations add ... -- --provider Postgres`. Generate `schema.sql` + `schema.postgres.sql` via `dotnet ef migrations script`. Embed both as resources in MyApp.csproj. Sets up the AOT-friendly schema artifacts the next commit will start using at runtime (replacing the current `MigrateAsync()` reflection-based path).

---

## Task 10 (ATOMIC with Task 11): za-vertical-slice — ApplyEmbeddedSchemaAsync + SchemaStrategy refactor

**Atomicity:** This Task and Task 11 (bench) MUST land in the same commit. Program.cs's new schema-strategy semantics and the bench's `Database:SchemaStrategy` config-set line are coupled.

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs`
- Modify: `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`

**Step 1:** Port `ApplyEmbeddedSchemaAsync` from za-clean to vertical-slice's Program.cs. Add the helper near the bottom of the file (after `app.Run()` is fine — it's a local function). Use the same implementation as za-clean's Task 5 (provider-aware, sqlite_master/to_regclass dual idempotency check), reading `typeof(Program).Assembly`.

**Step 2:** Replace the existing schema-application block in Program.cs. After PR #140 the block (currently around lines 128-141) is:

```csharp
// Apply schema on startup. `Database:SchemaStrategy` controls how:
//
//   Migrate         (default) — `MigrateAsync()`. Production behavior; the
//                   shipped Sqlite migrations apply. Postgres deployments
//                   must scaffold their own Postgres-typed migrations first
//                   (existing migrations declare Sqlite types).
//   EnsureCreated   — runtime-model schema creation via `EnsureCreatedAsync()`.
//                   Used by ad-hoc Postgres experimentation and the
//                   NBomber-against-Postgres load test. Bypasses migrations
//                   history; not appropriate for long-lived production
//                   Postgres deployments.
//   Skip            — startup does nothing. Used by WritePipelineBench's
//                   Postgres branch, which owns schema creation in
//                   [GlobalSetup].
var schemaStrategy = builder.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "Migrate";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (string.Equals(schemaStrategy, "EnsureCreated", StringComparison.OrdinalIgnoreCase))
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}
```

Replace with:

```csharp
// Apply schema on startup. `Database:SchemaStrategy` controls how:
//
//   EmbeddedScript  (default) — load schema.sql (Sqlite) or schema.postgres.sql
//                   (Postgres) from embedded resources and apply via raw ADO.NET.
//                   AOT-compatible — no reflection.
//   Skip            — startup does nothing. Used by WritePipelineBench's
//                   [GlobalSetup] paths where the bench owns DB lifecycle.
var schemaStrategy = builder.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplyEmbeddedSchemaAsync(db, dbProvider);
}
```

(`dbProvider` is from earlier in Program.cs.)

The `Migrate` / `EnsureCreated` enum values disappear. The reflection-based runtime paths go away.

**Step 3:** Add the `ApplyEmbeddedSchemaAsync` static helper (same implementation as za-clean's Task 5):

```csharp
static async Task ApplyEmbeddedSchemaAsync(AppDbContext db, string provider)
{
    var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);
    var resourceSuffix = isPostgres ? "schema.postgres.sql" : "schema.sql";

    var asm = typeof(Program).Assembly;
    var resourceName = asm.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
    using var stream = asm.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream);
    var script = await reader.ReadToEndAsync();

    var conn = db.Database.GetDbConnection();
    var openedHere = conn.State != System.Data.ConnectionState.Open;
    if (openedHere)
    {
        await conn.OpenAsync();
    }
    try
    {
        await using (var check = conn.CreateCommand())
        {
            check.CommandText = isPostgres
                ? "SELECT to_regclass('public.\"__EFMigrationsHistory\"');"
                : "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var exists = await check.ExecuteScalarAsync();
            var hasHistory = exists is not null && exists is not DBNull;
            if (hasHistory)
            {
                return;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = script;
        await cmd.ExecuteNonQueryAsync();
    }
    finally
    {
        if (openedHere)
        {
            await conn.CloseAsync();
        }
    }
}
```

**Step 4:** Update vertical-slice's `WritePipelineBench.cs` Postgres branch. Currently (post-PR #140), the bench's Postgres branch:
- Sets `["Database:SchemaStrategy"] = "Skip"` in the WAF config.
- Calls `db.Database.EnsureCreated()` externally after WAF builds, before `CreateClient` (around lines 162-167).

Change to:
- Set `["Database:Provider"] = "Postgres"` (added — not in the bench config today).
- Set `["Database:SchemaStrategy"] = "EmbeddedScript"` (the default, but be explicit).
- **Remove** the entire post-WAF-build external `EnsureCreated()` block.

Also for Sqlite branch:
- Set `["Database:Provider"] = "Sqlite"` (added).
- `["Database:SchemaStrategy"] = "EmbeddedScript"` is the same default.

The bench's `skipMigrate` local variable is no longer load-bearing in the config dict (both branches set the same SchemaStrategy now); it stays for code clarity but its only effect is naming the post-build EnsureCreated decision, which goes away. **Remove the `skipMigrate` local entirely.**

**Step 5:** Commit (atomic — both files).

```powershell
git add content/za-vertical-slice/src/MyApp/Program.cs content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs
git commit -m "refactor(za-vertical-slice): drop reflection-based runtime EF paths"
```

Body: Program.cs replaces `MigrateAsync` / `EnsureCreated` runtime calls with `ApplyEmbeddedSchemaAsync` (ported from za-clean) — provider-aware, runs against schema.sql or schema.postgres.sql, AOT-correct. `Database:SchemaStrategy` collapses from {Migrate, EnsureCreated, Skip} to {EmbeddedScript, Skip}. WritePipelineBench drops the external `db.Database.EnsureCreated()` call for its Postgres branch — WAF startup now applies the schema via the new code path. Both Sqlite and Postgres branches now go through the same production code path. Atomic with bench config update so PR #140's BDN run stays green across the rename.

---

## Task 11: vertical-slice — README + docs drift fixes + tools/regen-schema scripts

**Files:**
- Modify: `content/za-vertical-slice/README.md` (the "Swap SQLite → PostgreSQL" line — currently mentions `Database:SchemaStrategy=EnsureCreated`)
- Modify: `docs/za-vertical-slice.md` (line 50 setup-notes bullet)
- Create: `content/za-vertical-slice/tools/regen-schema.sh`
- Create: `content/za-vertical-slice/tools/regen-schema.ps1`

**Step 1:** Update `content/za-vertical-slice/README.md`. The "Swap SQLite → PostgreSQL" recipe currently mentions `Database:SchemaStrategy=EnsureCreated` for load-testing. Update to drop that value (no longer exists) and reference the regen-schema script:

Existing text (after PR #140):
```markdown
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. For load-testing or ad-hoc experimentation, also set `Database:SchemaStrategy=EnsureCreated` (creates the schema from the runtime model — bypasses migration history; not for long-lived production deployments). For production Postgres, scaffold proper migrations:
  ```bash
  dotnet ef migrations add InitialCreate --context AppDbContext --output-dir Persistence/Migrations.Postgres
  ```
  and leave `Database:SchemaStrategy` at its default (`Migrate`).
```

Replace with:
```markdown
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. Startup applies the embedded `schema.postgres.sql` via `ApplyEmbeddedSchemaAsync` — no EF reflection at runtime, so AOT publish stays clean (when the template's AOT opt-out lifts). After entity changes, regenerate both providers' migrations + schema scripts:
  ```bash
  tools/regen-schema.sh              # bash
  pwsh tools/regen-schema.ps1        # PowerShell
  ```
  This produces fresh `Migrations.Sqlite/`, `Migrations.Postgres/`, `schema.sql`, and `schema.postgres.sql` so both providers stay in lockstep.
```

**Step 2:** Update `docs/za-vertical-slice.md`'s line 50 (the WritePipelineBench setup-notes bullet that still references `Bench:SkipStartupMigrate`/`Migrate`/`EnsureCreated` semantics).

Find the existing text around line 50:
```markdown
- **SQLite** profile uses an in-memory connection (`DataSource=:memory:`); schema applied via the production `Program.cs` `MigrateAsync()` path.
- **Postgres** profile creates a fresh per-process database (`bench_<guid8>`) and applies the EF runtime model via `EnsureCreated()`. The bench sets `Database:SchemaStrategy=Skip` so `Program.cs`'s startup migration is bypassed (existing Sqlite-typed migrations don't translate to Postgres DDL).
```

Replace with:
```markdown
- **SQLite** profile uses an in-memory connection (`DataSource=:memory:`); schema applied via the production `Program.cs` `ApplyEmbeddedSchemaAsync` path reading `schema.sql`.
- **Postgres** profile creates a fresh per-process database (`bench_<guid8>`) and applies `schema.postgres.sql` via the same production path. AOT-friendly — no reflection. The bench sets `Database:Provider=Postgres`; Program.cs picks the right embedded resource.
```

**Step 3:** Create `content/za-vertical-slice/tools/regen-schema.sh`:

```bash
#!/usr/bin/env bash
# Regenerate both providers' migration histories + embedded schema scripts
# after entity changes. Run from the za-vertical-slice root.
#
# Usage: ./tools/regen-schema.sh [MigrationName]
#   MigrationName defaults to "Update".
set -euo pipefail
NAME="${1:-Update}"

cd "$(dirname "$0")/../src/MyApp"

dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Sqlite   -- --provider Sqlite
dotnet ef migrations add "$NAME" --context AppDbContext \
  --output-dir Persistence/Migrations.Postgres -- --provider Postgres
dotnet ef migrations script --context AppDbContext --idempotent \
  --output Persistence/schema.sql           -- --provider Sqlite
dotnet ef migrations script --context AppDbContext --idempotent \
  --output Persistence/schema.postgres.sql  -- --provider Postgres

echo "Regenerated migrations + schema scripts for both providers."
```

Note: only the `cd "$(dirname "$0")/../src/MyApp"` differs from za-clean's `cd ".../src/MyApp.Infrastructure"` — vertical-slice has the single MyApp csproj.

**Step 4:** Create the PowerShell variant `content/za-vertical-slice/tools/regen-schema.ps1` with the same single-csproj path adjustment.

**Step 5:** Commit.

```powershell
git add content/za-vertical-slice/README.md docs/za-vertical-slice.md content/za-vertical-slice/tools
git commit -m "docs(za-vertical-slice): drift fixes + regen-schema scripts"
```

Body: README's "Swap SQLite → PostgreSQL" no longer mentions the removed `Database:SchemaStrategy=EnsureCreated` value; now points at the new `tools/regen-schema.*` wrappers and notes AOT-readiness. `docs/za-vertical-slice.md`'s WritePipelineBench setup notes updated to describe the new embedded-script path. `tools/regen-schema.{sh,ps1}` ship the same 4-line recipe za-clean has.

---

## Task 12: Backlog updates

**Files:**
- Modify: `docs/backlog.md`

**Step 1:** Strike B3 with the cross-template sync note. Find the B3 entry; replace its heading with:

```markdown
## ~~B3 — Postgres bench profile for za-clean (replication)~~ — ✅ shipped 2026-05-29 (with cross-template sync)
```

Add a "Shipped:" paragraph documenting:
- BDN `[Params] DbBackend` profile added to za-clean's WritePipelineBench (1 method × 2 backends → 2 rows).
- Both templates ship `schema.sql` + `schema.postgres.sql` as embedded resources.
- `Database:SchemaStrategy` collapsed from PR #140's 3-value enum to 2 (`EmbeddedScript` / `Skip`); reflection-based runtime EF paths gone from both templates.
- `tools/regen-schema.{sh,ps1}` bundles the regen recipe.
- `nbomber-postgres-clean` CI job added alongside `nbomber-postgres-vs`.
- Vertical-slice's bench's external `EnsureCreated()` call removed; both templates' bench paths now use the same production schema code path.

**Step 2:** Mark B4 (if it exists in the backlog as a separate entry for NBomber-Postgres replication) as superseded:

```markdown
## ~~B4 — NBomber-Postgres mirror to za-clean~~ — ✅ superseded by B3 (same PR)
```

**Step 3:** Commit.

```powershell
git add docs/backlog.md
git commit -m "docs(backlog): strike B3 + mark B4 superseded"
```

Body: B3 shipped 2026-05-29. Bundle includes B4 (NBomber-Postgres on za-clean) as a single PR because the underlying SchemaStrategy + embedded-script refactor unifies cleanly across both templates.

---

## Task 13: Push, open PR, trigger workflow, verify

**Step 1:** Push the branch.

```powershell
git push -u origin feat/za-clean-postgres-mirror
```

**Step 2:** Open the PR.

```powershell
gh pr create --base main --head feat/za-clean-postgres-mirror --title "feat(za-clean): postgres mirror + cross-template SchemaStrategy sync" --body "$(cat <<'EOF'
## Summary

Graduates **B3** (za-clean replication of PR #140's Postgres + NBomber work) and pulls **za-vertical-slice** up to the same AOT-friendly embedded-script schema pattern, so the two templates' DB-config mental models converge.

## Highlights

- Both templates ship `schema.sql` + `schema.postgres.sql` as embedded resources. Runtime applies the right one based on `Database:Provider`. **Zero EF reflection at runtime in either template's production code path** — AOT-correct for za-clean (which is AOT-published), AOT-ready for vertical-slice (which currently opts out of AOT).
- `Database:SchemaStrategy` collapses from PR #140's 3-value enum (`Migrate` / `EnsureCreated` / `Skip`) to 2 (`EmbeddedScript` / `Skip`). The reflection-based runtime EF paths go away.
- Parallel `Migrations.Sqlite/` + `Migrations.Postgres/` folders per template; `DesignTimeDbContextFactory` takes a `--provider` arg.
- Bench refactor for both templates: za-clean's `WritePipelineBench` gains `[Params] DbBackend` (1×2 = 2 rows); vertical-slice's drops its external `EnsureCreated()` call (now goes through the same code path as production).
- New CI job `nbomber-postgres-clean` mirroring `nbomber-postgres-vs`. Artifacts: `nbomber-za-clean-postgres`, `nbomber-sut-log-clean`. The existing vs job's SUT log artifact renames to `nbomber-sut-log-vs` to disambiguate.
- `tools/regen-schema.{sh,ps1}` wrapper bundles the 4-line migrate + script regen recipe for adopters after entity changes.

## Design + plan docs

- `docs/plans/2026-05-29-za-clean-postgres-mirror-design.md`
- `docs/plans/2026-05-29-za-clean-postgres-mirror.md`

## Test plan

- [ ] CI build green
- [ ] Manual `Benchmarks (manual)` workflow produces:
  - 6 real rows for `bdn-za-vertical-slice-WritePipeline` (preserves PR #140)
  - 2 real rows for `bdn-za-clean-WritePipeline` (new — 1 method × 2 backends)
  - Real numbers in `nbomber-za-vertical-slice-postgres` (~2,540 RPS, preserves PR #140)
  - Real numbers in `nbomber-za-clean-postgres` (new)
- [ ] Numbers folded into `docs/za-clean.md` "Load testing against Postgres" Numbers section in a post-merge commit
EOF
)"
```

**Step 3:** Wait for CI build to pass.

```powershell
gh pr checks --watch
```

**Step 4:** Trigger the manual benchmark workflow.

```powershell
gh workflow run benchmarks.yml --ref feat/za-clean-postgres-mirror
```

**Step 5:** Wait ~15-20 min (the workflow now has 5 matrix legs + 2 NBomber jobs running in parallel). Harvest:

```powershell
$run = gh run list --workflow=benchmarks.yml --branch=feat/za-clean-postgres-mirror --limit 1 --json databaseId -q '.[0].databaseId'
gh run download $run -n bdn-za-vertical-slice-WritePipeline -D .bench-artifacts/b3/bdn-vs
gh run download $run -n bdn-za-clean-WritePipeline          -D .bench-artifacts/b3/bdn-clean
gh run download $run -n nbomber-za-vertical-slice-postgres  -D .bench-artifacts/b3/nbomber-vs
gh run download $run -n nbomber-za-clean-postgres           -D .bench-artifacts/b3/nbomber-clean
gh run download $run -n nbomber-sut-log-vs                  -D .bench-artifacts/b3/sut-vs
gh run download $run -n nbomber-sut-log-clean               -D .bench-artifacts/b3/sut-clean
```

**Step 6:** Verify.

- `bdn-za-vertical-slice-WritePipeline`: 6 real rows. RPS family preserved (~2,540 ± noise).
- `bdn-za-clean-WritePipeline`: 2 real rows (1 method × 2 backends).
- `nbomber-za-vertical-slice-postgres`: report shows RPS ~2,540 ± noise (preserves PR #140 ceiling).
- `nbomber-za-clean-postgres`: report shows real RPS + p50/p95/p99.
- Both SUT logs show clean startup (no exceptions, "Application started" present).

If any leg failed:
- SUT log dump first (most likely: schema-script issue, env-var case mismatch, or healthz timeout).
- BDN regression on vs: check the schema-strategy refactor preserved the bench's Sqlite path correctly.
- BDN regression on clean: check `typeof(AppDbContext)` is in the strip predicate.

**Step 7:** Comment on the PR.

```powershell
gh pr comment <PR#> --repo ZeroAlloc-Net/ZeroAlloc.Templates --body "$(cat <<'EOF'
## Verification ✅

[Paste bdn-za-clean-WritePipeline 2-row excerpt]

[Paste bdn-za-vertical-slice-WritePipeline 6-row excerpt — confirm preserved]

NBomber:

| Template | RPS | p50 | p95 | p99 | Fail % |
|---|---:|---:|---:|---:|---:|
| za-vertical-slice | ... | ... | ... | ... | ... |
| za-clean          | ... | ... | ... | ... | ... |

Both templates now share the same AOT-friendly embedded-script schema path. Ready to merge.
EOF
)"
```

**Step 8:** Hand off for user merge.

After merge, a follow-up `docs:` commit on `main` pastes the actual NBomber numbers into `docs/za-clean.md`'s "Load testing against Postgres" Numbers section placeholder.

---

## Out of scope

- **Production Postgres deployment guide** (secrets, monitoring, connection-pool tuning) — adopters take it from there.
- **AOT-Postgres smoke job** — the existing `aot-publish-smoke-clean` only exercises Sqlite. Adding Postgres-AOT smoke would catch trim/warn regressions earlier but adds CI time; deferred to a backlog item if Npgsql ships an AOT-breaking change.
- **Postgres 18** — Renovate PR `renovate/postgres-18.x` is in flight. Stay on `postgres:17` for this PR; merge that PR separately.
- **Per-provider connection-pool tuning** — `Maximum Pool Size=500` carried over from PR #140's NBomber experiment. Production adopters tune for their workload.
