# Postgres Bench Profile (B2) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Postgres-backed profile to `za-vertical-slice`'s `WritePipelineBench` so the three pipeline-attribution benchmarks run against both in-memory SQLite and a localhost Postgres, producing a single artifact with 6 comparison rows.

**Architecture:** Single bench class with `[Params(DbBackend.Sqlite, DbBackend.Postgres)]`. Sqlite path unchanged (keeps `Program.cs`'s `MigrateAsync` as the schema source — B1 fix preserved). Postgres path creates a fresh per-process database (`bench_<guid8>`) and applies the EF runtime model via `EnsureCreated()`; the bench sets a `Bench:SkipStartupMigrate` config flag so `Program.cs` doesn't try to apply Sqlite-typed migrations against Postgres.

**Tech Stack:** .NET 10, BenchmarkDotNet 0.15.x, EF Core 10, Npgsql.EntityFrameworkCore.PostgreSQL 9.x, Postgres 17 (GHA `services:` block + local `docker run`).

**Design doc:** `docs/plans/2026-05-28-postgres-bench-profile-design.md` (committed `90a68eb`).
**Branch:** `feat/postgres-bench-profile`.

---

## Conventions for this plan

- All paths relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.
- "Build" means `dotnet build -c Release` from the bench project; "run" means `dotnet run -c Release --no-build -- --filter "*WritePipelineBench*" --job Dry` (the `--job Dry` flag does a single iteration per benchmark — fast smoke check during development, real numbers come from the CI workflow at the end).
- Each task ends with a commit. Use conventional-commit prefixes (`feat:`, `chore:`, `refactor:`).
- No unit tests for the bench changes — verification is build + dry-run + final manual CI run (matches the B1 fix's verification pattern).

---

## Task 1: Add Npgsql package reference

**Files:**
- Modify: `Directory.Packages.props` (root) — line ~37, after `Microsoft.EntityFrameworkCore.Sqlite`
- Modify: `content/za-clean/Directory.Packages.props` — identical edit
- Modify: `content/za-vertical-slice/Directory.Packages.props` — identical edit
- Modify: `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj` — add `<PackageReference>`

**Step 1: Add `PackageVersion` to all three `Directory.Packages.props`**

In each of the three files, insert after the line containing `Microsoft.EntityFrameworkCore.Sqlite`:

```xml
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
```

(Use the latest stable 9.x available on nuget.org at implementation time. Check `https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json` for the current version. The version listed above is a reasonable default if API is unavailable — bump if newer 9.x exists.)

All three files must stay in sync. Root `Directory.Packages.props` governs the template's own build; per-template variants govern adopter projects after templating.

**Step 2: Add `PackageReference` to MyApp.Benchmarks**

In `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj`, after the line `<PackageReference Include="System.IdentityModel.Tokens.Jwt" />`:

```xml
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
```

**Step 3: Restore to verify the dep wires up**

Run from `content/za-vertical-slice/benchmarks/MyApp.Benchmarks`:

```powershell
dotnet restore
```

Expected: completes without `NU1605` (downgrade) or `NU1101` (not-found). EF Core 10 reference satisfies Npgsql 9's `>= EF Core 9` floor.

**Step 4: Commit**

```powershell
git add Directory.Packages.props content/za-clean/Directory.Packages.props content/za-vertical-slice/Directory.Packages.props content/za-vertical-slice/benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj
git commit -m "chore(deps): add Npgsql EF provider for postgres bench profile (B2)"
```

---

## Task 2: Add `Bench:SkipStartupMigrate` config flag in Program.cs

**Why this task exists:** `WritePipelineBench`'s Postgres branch needs the Postgres-configured `DbContext` to skip `Program.cs`'s startup `MigrateAsync()`. Existing migrations declare `INTEGER` / `TEXT` column types (`content/za-vertical-slice/src/MyApp/Persistence/Migrations/20260526204912_InitialCreate.cs:17-19`), which Npgsql doesn't accept — `MigrateAsync` on a Postgres context will throw.

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs:131-135`

**Step 1: Wrap the startup migration block in a flag check**

Current code (`Program.cs:128-135`):

```csharp
// Apply pending migrations on startup so a fresh dev box doesn't need a
// separate `dotnet ef database update` step. Safe to re-run — Migrate()
// is a no-op once the database is up to date.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

Replace with:

```csharp
// Apply pending migrations on startup so a fresh dev box doesn't need a
// separate `dotnet ef database update` step. Safe to re-run — Migrate()
// is a no-op once the database is up to date.
//
// `Bench:SkipStartupMigrate` is honoured by the WritePipelineBench Postgres
// profile, which substitutes a non-Sqlite DbContext at WebApplicationFactory
// time and creates the schema via EnsureCreated() instead. Production
// configuration never sets this flag.
if (!builder.Configuration.GetValue<bool>("Bench:SkipStartupMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

**Step 2: Build to verify**

Run from `content/za-vertical-slice`:

```powershell
dotnet build -c Release src/MyApp/MyApp.csproj
```

Expected: build succeeds. The flag check compiles against the existing `builder.Configuration` instance — no new using directives needed.

**Step 3: Commit**

```powershell
git add content/za-vertical-slice/src/MyApp/Program.cs
git commit -m "feat(myapp): opt-out flag for startup MigrateAsync (Bench:SkipStartupMigrate)"
```

---

## Task 3: Refactor WritePipelineBench to dispatch over DbBackend

**Files:**
- Modify: `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs` (whole-file rewrite of `[GlobalSetup]` / `[GlobalCleanup]`; benchmark methods unchanged)

**Step 1: Add the `DbBackend` enum and `[Params]` field**

Insert the enum at the bottom of the file (after the `WritePipelineBench` class closing brace):

```csharp
public enum DbBackend
{
    Sqlite,
    Postgres,
}
```

Inside `WritePipelineBench`, replace the existing field declarations (currently lines 40-44) with:

```csharp
[Params(DbBackend.Sqlite, DbBackend.Postgres)]
public DbBackend Backend { get; set; }

private WebApplicationFactory<Program>? _factory;
private HttpClient? _client;
private SqliteConnection? _connection;
private string? _postgresAdminConnString;
private string? _postgresDbName;
private object? _httpRequest;
private PlaceOrderCommand _command;
```

**Step 2: Rewrite `[GlobalSetup]` to branch on `Backend`**

Replace the entire `Setup()` method body (currently lines 47-89). The shared parts (HTTP request payload, command, JWT auth header, `WithWebHostBuilder` skeleton) factor out; the DbContext override and the `Bench:SkipStartupMigrate` flag are the per-branch pieces:

```csharp
[GlobalSetup]
public void Setup()
{
    var skipMigrate = Backend == DbBackend.Postgres;

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

        _postgresAdminConnString = $"Host={host};Port={port};Username={user};Password={pwd};Database={adminDb}";
        _postgresDbName = "bench_" + Guid.NewGuid().ToString("N")[..8];

        using var admin = new Npgsql.NpgsqlConnection(_postgresAdminConnString);
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
            ["Bench:SkipStartupMigrate"] = skipMigrate ? "true" : "false",
        }));
        b.ConfigureServices(s =>
        {
            var dbDescriptor = s.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
            {
                s.Remove(dbDescriptor);
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
                var workerConnString =
                    $"{_postgresAdminConnString!.Replace($"Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bench"}", $"Database={_postgresDbName}", StringComparison.Ordinal)}";
                s.AddDbContext<AppDbContext>(opt =>
                {
                    opt.UseNpgsql(workerConnString);
                    opt.ConfigureWarnings(w => w.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                });
            }
        });
    });

    if (Backend == DbBackend.Postgres)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    _client = _factory.CreateClient();
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", TestJwt.Issue(["orders.write"]));

    _httpRequest = new { customerId = 42, total = 99.99m };
    _command = new PlaceOrderCommand(CustomerId: new CustomerId(42), Total: 99.99m);
}
```

Note: the Postgres `EnsureCreated()` call happens *after* `WebApplicationFactory` is built but *before* `CreateClient()` — this triggers Program.cs's startup pipeline (which now skips `MigrateAsync` thanks to the flag), then writes the schema directly from the runtime model.

**Step 3: Rewrite `[GlobalCleanup]`**

Replace the existing `Cleanup()` method (currently lines 134-140):

```csharp
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
            using var admin = new Npgsql.NpgsqlConnection(_postgresAdminConnString);
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
```

`WITH (FORCE)` (Postgres 13+) terminates any lingering connection holders before drop. The empty `catch` is the *only* place in this file where swallowing the exception is the right call — failing cleanup must not poison the bench report.

**Step 4: Update the class-level XML doc to document the Postgres requirement**

Replace the existing class summary (currently lines 20-36) with:

```csharp
/// <summary>
/// Attribution benchmark for the PlaceOrder slice. Three measurements peel
/// the pipeline back one layer at a time so the cost of each layer falls
/// out as a delta:
/// <list type="bullet">
///   <item><description><c>PlaceOrder_FullPipeline</c> — HTTP → JWT → endpoint policy → mediator [RequirePolicy] → [Validate] → handler → EF.</description></item>
///   <item><description><c>PlaceOrder_MediatorDirect</c> — mediator [RequirePolicy] → [Validate] → handler → EF (HTTP + JWT bypassed; HttpContext pre-populated with the scope claim so authorization sees an authenticated principal).</description></item>
///   <item><description><c>PlaceOrder_HandlerDirect</c> — handler → EF (mediator, validation, authorization all bypassed; raw handler invocation against the scoped DbContext).</description></item>
/// </list>
/// <para>
/// <b>Reading the deltas:</b> (Full − MediatorDirect) is the cost of the HTTP
/// + JWT + JSON-deserialization layer. (MediatorDirect − HandlerDirect) is the
/// cost of mediator dispatch + validation pipeline + authorization pipeline.
/// HandlerDirect itself is the EF baseline.
/// </para>
/// <para>
/// <b>Backends:</b> <c>[Params]</c> dispatches each method against both
/// in-memory SQLite and a localhost Postgres. Sqlite uses the production
/// schema path (<c>Program.cs</c>'s <c>MigrateAsync</c>). Postgres uses
/// <c>EnsureCreated()</c> against a fresh per-process database
/// (<c>bench_&lt;guid8&gt;</c>) — existing Sqlite-typed migrations don't
/// translate, and the bench profile owns its schema path exclusively.
/// </para>
/// <para>
/// <b>Local dev — Postgres profile only:</b>
/// <code>
/// docker run --rm -d -p 5432:5432 \
///   -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
///   --name bench-pg postgres:17
/// </code>
/// then <c>dotnet run -c Release -- --filter "*WritePipelineBench*"</c>.
/// CI provisions Postgres via the <c>services:</c> block in
/// <c>.github/workflows/benchmarks.yml</c>.
/// </para>
/// </summary>
```

**Step 5: Build + dry-run smoke check**

Run from `content/za-vertical-slice/benchmarks/MyApp.Benchmarks`:

```powershell
dotnet build -c Release
```

Expected: build succeeds. No `MA0048` (file name vs type name — the new `DbBackend` enum lives in `WritePipelineBench.cs`, which is fine because `NoWarn` includes `MA0048` per `MyApp.Benchmarks.csproj:10`).

If a local Postgres is running on `localhost:5432` with `postgres/postgres/bench`, do a full dry-run:

```powershell
dotnet run -c Release --no-build -- --filter "*WritePipelineBench*" --job Dry
```

Expected: 6 rows in the report — `PlaceOrder_FullPipeline / Sqlite`, `PlaceOrder_FullPipeline / Postgres`, etc. `--job Dry` runs one iteration per row in ~5 seconds total.

If Postgres isn't running locally, the dry-run will fail on the Postgres rows with a connection error — that's expected and proves the error message surfaces clearly. Run with `--job Dry --filter "*Sqlite*"` (if BDN supports param filtering) or accept the partial run; CI is the source of truth.

**Step 6: Commit**

```powershell
git add content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs
git commit -m "feat(za-vertical-slice/bench): add Postgres profile via [Params] DbBackend (B2)"
```

---

## Task 4: Provision Postgres in benchmarks.yml

**Files:**
- Modify: `.github/workflows/benchmarks.yml:21-22` (add `services:` block to the `benchmark` job)
- Modify: `.github/workflows/benchmarks.yml:64-65` (add `env:` to the Run BenchmarkDotNet step)

**Step 1: Add the `services:` block**

After the `timeout-minutes: 30` line (line 22), and before `strategy:` (line 23), insert:

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

The 4-space indent must match the job key's indent level — `services:` is a sibling of `runs-on:`, `timeout-minutes:`, and `strategy:`.

**Step 2: Add `env:` to the Run BenchmarkDotNet step**

The Run step (currently lines 63-65) becomes:

```yaml
      - name: Run BenchmarkDotNet
        working-directory: content/${{ matrix.template }}/${{ matrix.project }}
        env:
          POSTGRES_HOST: localhost
          POSTGRES_PORT: "5432"
          POSTGRES_DB: bench
          POSTGRES_USER: postgres
          POSTGRES_PASSWORD: postgres
        run: dotnet run -c Release --no-build -- --filter "${{ matrix.filter }}" --exporters github
```

Yes, the non-WritePipeline matrix legs receive these env vars even though they ignore them — accepted overhead per the design doc (alternative is per-leg conditional env, which adds complexity for no real gain).

**Step 3: Lint the YAML locally**

```powershell
dotnet tool run --version 2>$null  # confirm dotnet tool host works; skip if no yaml-lint
```

There's no project-local YAML linter. Visually verify the indentation: `services:` and `strategy:` should align under `runs-on:`. GHA will reject malformed YAML at run time with a clear error.

**Step 4: Commit**

```powershell
git add .github/workflows/benchmarks.yml
git commit -m "ci(bench): provision postgres:17 service container for WritePipeline (B2)"
```

---

## Task 5: Push branch, open PR, trigger manual workflow, verify

**Step 1: Push the branch**

```powershell
git push -u origin feat/postgres-bench-profile
```

**Step 2: Open the PR**

```powershell
gh pr create --base main --head feat/postgres-bench-profile --title "feat(za-vertical-slice/bench): postgres bench profile (B2)" --body "$(cat <<'EOF'
## Summary

Graduates **B2** (`docs/backlog.md`). Adds a Postgres-backed profile to za-vertical-slice's `WritePipelineBench` so the three pipeline-attribution benchmarks run against both SQLite (in-memory) and Postgres (localhost via GHA `services:`).

## Changes

- **Bench class**: `[Params(DbBackend.Sqlite, DbBackend.Postgres)]` cross-products `Method × Backend` → 6 rows per artifact. Sqlite branch unchanged (B1 fix preserved — `Program.cs`'s `MigrateAsync` is still the schema source for the Sqlite path). Postgres branch creates a fresh `bench_<guid8>` database per process and applies the runtime model via `EnsureCreated()`.
- **Program.cs**: introduces a `Bench:SkipStartupMigrate` config flag (production never sets it). The bench sets it only when `Backend == Postgres`, so the Sqlite-typed migrations don't run against Postgres.
- **CI**: `benchmarks.yml` gains a `services: postgres:17` block + env vars. All 4 matrix legs receive the container; only the za-vertical-slice WritePipeline leg uses it.
- **Package**: adds `Npgsql.EntityFrameworkCore.PostgreSQL 9.x` to all 3 `Directory.Packages.props` and references it from `MyApp.Benchmarks.csproj`.

za-clean replication deferred (per design doc Q1).

## Design doc

`docs/plans/2026-05-28-postgres-bench-profile-design.md`

## Test plan

- [ ] CI build job (`build`, `build-vs`) green
- [ ] Manual `Benchmarks (manual)` workflow run on this branch produces 6 real rows for `bdn-za-vertical-slice-WritePipeline`
- [ ] Numbers folded into `docs/za-vertical-slice.md` in a follow-up post-merge commit

EOF
)"
```

**Step 3: Wait for CI build to pass**

Watch the auto-triggered CI workflow (build + build-vs + aot-publish-smoke). All must be green before triggering the manual benchmark workflow.

```powershell
gh pr checks --watch
```

**Step 4: Trigger the manual benchmark workflow**

```powershell
gh workflow run benchmarks.yml --ref feat/postgres-bench-profile
```

**Step 5: Wait, then harvest results**

After ~10-15 min, download the WritePipeline artifact:

```powershell
$run = gh run list --workflow=benchmarks.yml --branch=feat/postgres-bench-profile --limit 1 --json databaseId -q '.[0].databaseId'
gh run download $run -n bdn-za-vertical-slice-WritePipeline -D .bench-artifacts/postgres-profile-run
```

**Step 6: Verify**

Open `.bench-artifacts/postgres-profile-run/results/MyApp.Benchmarks.WritePipelineBench-report-github.md`. It must contain 6 rows:

| Method | Backend | Mean | ... |
|---|---|---|---|
| PlaceOrder_FullPipeline | Sqlite | ... | ... |
| PlaceOrder_FullPipeline | Postgres | ... | ... |
| PlaceOrder_MediatorDirect | Sqlite | ... | ... |
| PlaceOrder_MediatorDirect | Postgres | ... | ... |
| PlaceOrder_HandlerDirect | Sqlite | ... | ... |
| PlaceOrder_HandlerDirect | Postgres | ... | ... |

All six rows must show real `Mean` numbers — no `NA`, no `?`. Postgres rows will be absolutely higher than Sqlite (probably 5-20× — localhost network + WAL flushes) but the deltas between the three methods should remain readable within each backend.

If any row is `NA`: stop and investigate. Common causes:
- `services:` indentation wrong → CI silently runs without Postgres → `EnsureCreated` connection refused.
- `Bench:SkipStartupMigrate` not honoured → `MigrateAsync` runs against Npgsql → exception → bench fails on Postgres rows.
- Database name collision (very unlikely with guid8) → `CREATE DATABASE` fails.

**Step 7: Report ready for merge**

Comment on the PR with the 6-row table excerpt and a "ready to merge" note. Hand off for user merge.

---

## Out of scope

- `docs/za-vertical-slice.md` creation. Doc-paste happens *after* merge in a follow-up commit on `main`, once the workflow runs on the merged commit and produces canonical numbers (same flow as PR #133 refreshing numbers after PR #131 added the workflow).
- `za-clean` replication. Deferred until the vertical-slice numbers prove the framework-cost narrative is informative.
- Removing the now-redundant `services:` container from non-WritePipeline matrix legs. Accepted per design doc Section 2.
