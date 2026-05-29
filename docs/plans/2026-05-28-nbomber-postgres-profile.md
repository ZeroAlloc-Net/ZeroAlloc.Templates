# NBomber-against-Postgres Profile Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Layer NBomber-against-Postgres onto PR #140 alongside B2's BDN profile, so the load test exercises a real DB instead of in-memory SQLite (capped at ~470 RPS by SQLite's single-process lock).

**Architecture:** Production app gets an explicit `Database:Provider` config switch (Sqlite|Postgres) replacing the hardcoded `UseSqlite`. The B2-era `Bench:SkipStartupMigrate` bool consolidates into a richer `Database:SchemaStrategy` config (Migrate|EnsureCreated|Skip). NBomber gets a new dedicated CI job (not a matrix leg — the steps differ enough that nesting would force ugly `if:` guards) and a local docker-run recipe. Sqlite stays the zero-setup quickstart default.

**Tech Stack:** .NET 10, EF Core 10 (`Microsoft.EntityFrameworkCore.Sqlite` already in production csproj; adding `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2` which is already in `Directory.Packages.props` from B2). NBomber 6.4.1. Postgres 17.

**Design doc:** `docs/plans/2026-05-28-nbomber-postgres-profile-design.md` (committed `960299f`).
**Branch:** `feat/postgres-bench-profile` (PR #140 — already open, B2 commits already on the branch).

---

## Conventions for this plan

- All paths relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.
- "Build" means `dotnet build -c Release` from the relevant project; verification stays CI-driven per the standing SDK pin (`global.json` 10.0.300 vs installed 10.0.204).
- Each task ends with a commit using conventional-commit prefixes (`feat:`, `chore:`, `refactor:`, `ci:`, `docs:`). Use the repo's `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer via HEREDOC.
- No unit tests for these changes — verification is CI build green + the manual benchmark workflow producing a `nbomber-za-vertical-slice-postgres` artifact with real numbers.
- **Task 3 is the only atomic-coupling task**: Program.cs's startup migration block and `WritePipelineBench.cs`'s config-set line must change in the **same commit**, because the bench's `Bench:SkipStartupMigrate=true` would stop being honored the moment Program.cs starts looking at `Database:SchemaStrategy` instead. A split would temporarily break B2's BDN run.

---

## Task 1: Add Npgsql package reference to production MyApp.csproj

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/MyApp.csproj` — add `<PackageReference>` in the EF Core block

**Step 1: Add `PackageReference`**

In `content/za-vertical-slice/src/MyApp/MyApp.csproj`, find the EF Core block (currently `Microsoft.EntityFrameworkCore.Sqlite` at line 30 + `Microsoft.EntityFrameworkCore.Design` at lines 31-34). After the `Microsoft.EntityFrameworkCore.Sqlite` line, insert:

```xml
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
```

The version is already centrally managed in `Directory.Packages.props` (10.0.2, added in B2 Task 1). No version attribute on the `PackageReference`.

**Step 2: Restore to verify**

Run from `content/za-vertical-slice/src/MyApp`:

```powershell
dotnet restore
```

Expected: completes without NU1605/NU1101. Note: local restore may hit the SDK pin (global.json 10.0.300 vs installed 10.0.204) — that's environmental, not a regression. CI is source of truth.

**Step 3: Commit**

```powershell
git add content/za-vertical-slice/src/MyApp/MyApp.csproj
git commit -m "chore(deps): add Npgsql to production MyApp.csproj"
```

Body:
```
Production MyApp gains the Npgsql EF provider so Program.cs can register
UseNpgsql when Database:Provider=Postgres. Default SQLite path unchanged;
adopter quickstart still requires zero setup. ~2 MB of additional disk
footprint, only loaded when Postgres is selected.

Follow-up of B2: the bench-side Npgsql wire-up landed in MyApp.Benchmarks.csproj;
this lands the production-side reference for the NBomber-against-Postgres path.
```

---

## Task 2: Add `Database:Provider` config switch in Program.cs

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs` — the `EF Core / SQLite` block (currently lines 38-54)

**Step 1: Replace the hardcoded `UseSqlite` block**

Current code (`Program.cs:38-54`):

```csharp
// ---------------------------------------------------------------------------
// EF Core / SQLite.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    opts.UseSqlite(connectionString);
    // EF Core 10 fires PendingModelChangesWarning by default when the runtime
    // model snapshot differs from the most recent migration's snapshot — and
    // produces false positives when the compiled-model path (UseModel) is in
    // use alongside committed migrations. Suppressed because regenerating
    // migrations on every CI run isn't a viable workflow for a template.
    opts.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
```

Replace with:

```csharp
// ---------------------------------------------------------------------------
// EF Core — provider selected by `Database:Provider` config (Sqlite|Postgres).
// Default is Sqlite so the zero-setup `dotnet run` quickstart still works
// (`Data Source=app.db` flows into UseSqlite). To target Postgres, set
// `Database:Provider=Postgres` and `ConnectionStrings:Default=Host=...;...`.
// ---------------------------------------------------------------------------
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        opts.UseNpgsql(connectionString);
    }
    else
    {
        opts.UseSqlite(connectionString);
    }
    // EF Core 10 fires PendingModelChangesWarning by default when the runtime
    // model snapshot differs from the most recent migration's snapshot — and
    // produces false positives when the compiled-model path (UseModel) is in
    // use alongside committed migrations. Suppressed because regenerating
    // migrations on every CI run isn't a viable workflow for a template.
    opts.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
```

**Step 2: Verify no new using directives are needed**

`UseNpgsql` is an extension method on `DbContextOptionsBuilder` from `Npgsql.EntityFrameworkCore.PostgreSQL`. Once the package reference is added (Task 1), the method is in scope without an explicit `using Npgsql.EntityFrameworkCore.PostgreSQL;` because `DbContextOptionsBuilder` itself already lives in `Microsoft.EntityFrameworkCore` which is brought in by the `Microsoft.EntityFrameworkCore.Sqlite` reference (transitively pulls `Microsoft.EntityFrameworkCore`). The extension method resolves through type lookup, not namespace lookup, but for grep-friendliness consider adding the explicit `using` — match the pattern of `using Microsoft.EntityFrameworkCore;` already at the top of the file.

If the file doesn't already have `using Microsoft.EntityFrameworkCore;`, add it. Otherwise nothing to change.

**Step 3: Build verification (CI-driven)**

Local build likely hits the SDK pin. Skip; CI will validate on push at Task 7 time.

**Step 4: Commit**

```powershell
git add content/za-vertical-slice/src/MyApp/Program.cs
git commit -m "feat(myapp): explicit Database:Provider config (Sqlite|Postgres)"
```

Body:
```
Program.cs now dispatches between UseSqlite and UseNpgsql based on an
explicit `Database:Provider` config key. Default is Sqlite, so the
quickstart `dotnet run` keeps working with zero setup. To target Postgres,
adopter sets `Database:Provider=Postgres` and points `ConnectionStrings:Default`
at the Postgres conn string (typically via env: `Database__Provider=Postgres`,
`ConnectionStrings__Default=Host=...;...`).

Rejected connection-string-prefix sniffing in favor of an explicit knob —
clearer reads in config files, loud failures when misconfigured, future-proof
for SqlServer/MySQL via a third else-branch.

Capability gating, no behavior change for SQLite default users. Production
adopters going to Postgres also need scaffolded Postgres-typed migrations
(or `Database:SchemaStrategy=EnsureCreated` for ad-hoc/load-test use), per
the follow-up commit.
```

---

## Task 3: Consolidate `Bench:SkipStartupMigrate` → `Database:SchemaStrategy`

**This is the only atomic-coupling task.** Program.cs and WritePipelineBench.cs must change in the same commit.

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs` (startup migration block, currently lines 128-141)
- Modify: `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs` (config dictionary, currently line 113)

**Step 1: Update Program.cs startup migration block**

Current code (lines 128-141):

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

Replace with:

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

**Step 2: Update WritePipelineBench config**

Current code (`WritePipelineBench.cs:113`):

```csharp
["Bench:SkipStartupMigrate"] = skipMigrate ? "true" : "false",
```

Replace with:

```csharp
["Database:SchemaStrategy"] = skipMigrate ? "Skip" : "Migrate",
```

Verify semantics:
- Sqlite branch (`skipMigrate=false`) → `Database:SchemaStrategy=Migrate` → Program.cs calls `MigrateAsync()` — B1 fix preserved.
- Postgres branch (`skipMigrate=true`) → `Database:SchemaStrategy=Skip` → Program.cs does nothing — bench's `[GlobalSetup]` then calls `db.Database.EnsureCreated()` directly, as today.

**Step 3: Verify the local `skipMigrate` variable name is still accurate**

The variable is computed earlier in `Setup()`:

```csharp
var skipMigrate = Backend == DbBackend.Postgres;
```

The name is still fine semantically — it captures whether to skip the production migration path. Could rename to `useSkipStrategy` or `useEnsureCreatedStrategy` but those obscure the intent. Leave it.

**Step 4: Commit**

Single atomic commit covering both files:

```powershell
git add content/za-vertical-slice/src/MyApp/Program.cs content/za-vertical-slice/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs
git commit -m "refactor(myapp): consolidate Bench:SkipStartupMigrate -> Database:SchemaStrategy"
```

Body:
```
The B2-era `Bench:SkipStartupMigrate` bool was too narrow — it only modeled
"skip vs. migrate", and the NBomber-against-Postgres path needs a third
mode ("EnsureCreated"). Consolidating into a single `Database:SchemaStrategy`
config key with three explicit states avoids a flag-interaction matrix.

States:
  Migrate         (default) — production. Runs MigrateAsync.
  EnsureCreated   — ad-hoc Postgres (load-test, experimentation). Runs
                    EnsureCreatedAsync against the runtime model.
                    Bypasses migrations history; not for long-lived
                    production deployments.
  Skip            — bench-owned schema. Program.cs does nothing at startup.

Atomic rename of the bench's config-set line in the same commit so the
B2 BDN-Postgres run stays green — the bench's Postgres branch now sets
"Skip" (semantically identical to the prior `SkipStartupMigrate=true`).
The Sqlite branch sets "Migrate" (semantically identical to the prior
`SkipStartupMigrate=false`).

The old flag had one consumer (the bench, ~24h old). No external callers
to deprecate.
```

---

## Task 4: Add `nbomber-postgres-vs` job to benchmarks.yml

**Files:**
- Modify: `.github/workflows/benchmarks.yml` — add a new top-level job + update the file-header comment

**Step 1: Update the file-header comment**

Current header (lines 1-13):

```yaml
name: Benchmarks (manual)

# Manual BenchmarkDotNet runs for both templates' Primitives + WritePipeline
# suites. Triggered via the GitHub Actions UI ("Run workflow"). Each matrix
# leg uploads its BenchmarkDotNet.Artifacts as a workflow artifact — download
# the bdn-* artifacts to harvest the per-method tables and paste them into
# docs/za-clean.md / docs/za-vertical-slice.md / READMEs.
#
# Skipped intentionally:
#   - The NBomber MyApp.LoadTest scenario (500 VUs against real Kestrel; fine
#     locally, fragile on shared CI runners).
#   - AOT-published benchmarks (the existing aot-smoke job already proves
#     AOT compatibility; JIT numbers are the regression net).
```

Replace with:

```yaml
name: Benchmarks (manual)

# Manual BenchmarkDotNet + NBomber runs for both templates. Triggered via
# the GitHub Actions UI ("Run workflow"). Each leg uploads its artifacts —
# bdn-* for BenchmarkDotNet, nbomber-* for NBomber.
#
# Two jobs:
#   - benchmark (matrix): BDN Primitives + WritePipeline for both templates.
#     The za-vertical-slice/WritePipeline leg cross-products SQLite + Postgres
#     via WritePipelineBench's [Params] DbBackend.
#   - nbomber-postgres-vs: za-vertical-slice load test against real Postgres.
#     Distinct from the BDN matrix because it spins up the SUT as a separate
#     process. Postgres is the only sensible load-test target — SQLite's
#     single-process file lock caps at ~470 RPS. The CI-fragility concern
#     that originally kept NBomber out of CI is mitigated by colocation
#     (SUT runs on the same runner as NBomber, no cross-host network).
#     If the job becomes a frequent retry-magnet, demote to manual-only.
#
# Skipped intentionally:
#   - AOT-published benchmarks (the existing aot-smoke job already proves
#     AOT compatibility; JIT numbers are the regression net).
```

**Step 2: Add the new job**

Append to the bottom of the file (after the existing `benchmark` job, currently ending at line 99):

```yaml

  nbomber-postgres-vs:
    name: za-vertical-slice / NBomber-Postgres
    runs-on: ubuntu-latest
    timeout-minutes: 30
    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_PASSWORD: postgres
          POSTGRES_DB: myapp_load
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 5s
          --health-timeout 3s
          --health-retries 5
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Build SUT
        working-directory: content/za-vertical-slice
        run: dotnet build src/MyApp -c Release

      - name: Build LoadTest
        working-directory: content/za-vertical-slice
        run: dotnet build benchmarks/MyApp.LoadTest -c Release

      - name: Start SUT against Postgres
        working-directory: content/za-vertical-slice
        env:
          Database__Provider: Postgres
          Database__SchemaStrategy: EnsureCreated
          ConnectionStrings__Default: "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load"
          ASPNETCORE_URLS: "http://localhost:5000"
        run: |
          nohup dotnet run -c Release --no-build --project src/MyApp > sut.log 2>&1 &
          echo $! > sut.pid

      - name: Wait for /healthz
        run: |
          for i in {1..60}; do
            if curl -fs http://localhost:5000/healthz > /dev/null; then
              echo "SUT healthy after ${i}s"
              exit 0
            fi
            sleep 1
          done
          echo "SUT did not become healthy in 60s — dumping log"
          cat content/za-vertical-slice/sut.log
          exit 1

      - name: Run NBomber
        working-directory: content/za-vertical-slice
        run: dotnet run -c Release --no-build --project benchmarks/MyApp.LoadTest -- http://localhost:5000

      - name: Stop SUT
        if: always()
        working-directory: content/za-vertical-slice
        run: |
          if [ -f sut.pid ]; then
            kill "$(cat sut.pid)" || true
          fi

      - name: Upload NBomber reports
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: nbomber-za-vertical-slice-postgres
          path: content/za-vertical-slice/reports/**
          if-no-files-found: warn
          retention-days: 30

      - name: Upload SUT log
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: nbomber-sut-log
          path: content/za-vertical-slice/sut.log
          if-no-files-found: warn
          retention-days: 7
```

Note: NBomber writes its reports under a `reports/` directory by convention. If `MyApp.LoadTest/Program.cs` overrides this, adjust the artifact path. As-shipped, NBomber's default is `reports/<timestamp>/` relative to the working directory — which is `content/za-vertical-slice` per the `Run NBomber` step's `working-directory`. The wildcard `reports/**` catches whichever timestamp directory the run produces.

**Step 3: Lint the YAML (best-effort visual)**

Read the file end-to-end. Verify:
- `nbomber-postgres-vs` is at 2-space indent (sibling of `benchmark`).
- Under it, `name:`/`runs-on:`/`timeout-minutes:`/`services:`/`steps:` are at 4 spaces.
- Step entries (`- name:`) are at 6 spaces; their child keys at 8 spaces.

GHA validates YAML at workflow run time. If anything's malformed it fails with a clear pointer.

**Step 4: Commit**

```powershell
git add .github/workflows/benchmarks.yml
git commit -m "ci(nbomber): add nbomber-postgres-vs job (real-DB load test)"
```

Body:
```
New top-level job — not a matrix leg under `benchmark` because the steps
differ enough that nesting would force ugly `if:` guards. Spins up
Postgres as a service container, builds the SUT and LoadTest projects,
launches the SUT as a background process with Database:Provider=Postgres +
Database:SchemaStrategy=EnsureCreated, waits for /healthz, runs NBomber
against localhost:5000, uploads NBomber reports + SUT log as artifacts.

The original benchmarks.yml header skipped NBomber as "fragile on shared
CI runners". Co-locating SUT and NBomber on the same runner removes
cross-host network variance, and the existing ReadHotPathScenario is
configured for measured throughput rather than max-VU stress. If the
job becomes a frequent retry-magnet, demote to manual-only.
```

---

## Task 5: Update README's "Swap SQLite → PostgreSQL" recipe + LoadTest top comment

**Files:**
- Modify: `content/za-vertical-slice/README.md:137` (the existing one-line recipe)
- Modify: `content/za-vertical-slice/benchmarks/MyApp.LoadTest/Program.cs` (add top-of-file comment)

**Step 1: Update README recipe**

Current line 137:

```markdown
- **Swap SQLite → PostgreSQL**: change `UseSqlite` to `UseNpgsql` in `Program.cs`, add the EF provider, regenerate migrations.
```

Replace with:

```markdown
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. For load-testing or ad-hoc experimentation, also set `Database:SchemaStrategy=EnsureCreated` (creates the schema from the runtime model — bypasses migration history; not for long-lived production deployments). For production Postgres, scaffold proper migrations:
  ```bash
  dotnet ef migrations add InitialCreate --context AppDbContext --output-dir Persistence/Migrations.Postgres
  ```
  and leave `Database:SchemaStrategy` at its default (`Migrate`).
```

**Step 2: Add top-of-file comment to MyApp.LoadTest/Program.cs**

Current file (8 lines, top is `using MyApp.LoadTest;`):

```csharp
using MyApp.LoadTest;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var token = TestJwt.Issue(["orders.read", "orders.write"]);

var scenario = ReadHotPathScenario.Build(baseUrl, token);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();
```

Prepend a comment block before the `using` directives:

```csharp
// NBomber load test for the PlaceOrder hot path.
//
// Local recipe — run against Postgres (Sqlite's single-process file lock
// caps the SUT at ~470 RPS, not a meaningful production signal):
//
//   docker run --rm -d -p 5432:5432 \
//     -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
//     --name myapp-load-pg postgres:17
//
//   Database__Provider=Postgres \
//   Database__SchemaStrategy=EnsureCreated \
//   ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load" \
//   dotnet run -c Release --project src/MyApp &
//
//   until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
//   dotnet run -c Release --project benchmarks/MyApp.LoadTest
//
//   kill %1; docker stop myapp-load-pg
//
// CI: the `nbomber-postgres-vs` job in .github/workflows/benchmarks.yml
// runs this end-to-end on every manual workflow trigger and uploads the
// NBomber report as the `nbomber-za-vertical-slice-postgres` artifact.

using MyApp.LoadTest;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var token = TestJwt.Issue(["orders.read", "orders.write"]);

var scenario = ReadHotPathScenario.Build(baseUrl, token);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();
```

**Step 3: Commit**

```powershell
git add content/za-vertical-slice/README.md content/za-vertical-slice/benchmarks/MyApp.LoadTest/Program.cs
git commit -m "docs(za-vertical-slice): NBomber+Postgres recipe in README + LoadTest top comment"
```

Body:
```
README's "Swap SQLite → PostgreSQL" recipe now references the explicit
Database:Provider knob (replacing the prior "change UseSqlite to UseNpgsql"
instruction, which is no longer accurate post-PR #140).

LoadTest/Program.cs gains a top-of-file comment with the docker-run +
SUT-launch + healthz-wait + NBomber-invoke recipe, so adopters who land
in the file directly see the full local workflow without round-tripping
to docs/za-vertical-slice.md.
```

---

## Task 6: Add "Load testing against Postgres" section to docs/za-vertical-slice.md

**Files:**
- Modify: `docs/za-vertical-slice.md` — append a new section after the existing "Reproducing locally" section

**Step 1: Append the new section**

Open `docs/za-vertical-slice.md` (created in B2's docs commit `78e3dd8`). Find the end of the file — currently the last section is "Reproducing locally" with the BDN docker-run recipe.

Append:

```markdown

## Load testing against Postgres

NBomber's `MyApp.LoadTest` previously targeted in-memory SQLite via the production app — capped at ~470 RPS by SQLite's single-process file lock. That ceiling is the lock, not the framework. Running against Postgres reveals the real throughput.

The SUT and NBomber run as separate processes. The SUT is configured for Postgres via env vars; NBomber's scenario code is unchanged.

### Local recipe

```bash
# 1. Start Postgres
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
  --name myapp-load-pg postgres:17

# 2. Start the SUT
Database__Provider=Postgres \
Database__SchemaStrategy=EnsureCreated \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load" \
dotnet run -c Release --project src/MyApp &

# 3. Wait for /healthz, then run NBomber
until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
dotnet run -c Release --project benchmarks/MyApp.LoadTest

# Cleanup
kill %1; docker stop myapp-load-pg
```

`Database__SchemaStrategy=EnsureCreated` bypasses migrations history — the SUT creates the schema directly from the EF runtime model. That's fine for load-testing (ephemeral DB, throwaway state). Production deployments should scaffold Postgres-typed migrations and switch back to the default `Migrate` strategy.

### CI

The `nbomber-postgres-vs` job in `.github/workflows/benchmarks.yml` runs the recipe above end-to-end on every manual workflow trigger. Artifacts:

- `nbomber-za-vertical-slice-postgres` — NBomber's HTML / CSV / Markdown reports.
- `nbomber-sut-log` — the SUT's stdout/stderr (kept short, 7-day retention).

### Numbers

> Filled in after the first post-merge CI run on `main`. Same flow as the BDN numbers above (PR #140 lands the harness; a follow-up commit on `main` pastes the numbers + a comparison-to-SQLite paragraph from the production-app side).
```

**Step 2: Commit**

```powershell
git add docs/za-vertical-slice.md
git commit -m "docs(za-vertical-slice): add Load testing against Postgres section"
```

Body:
```
New section appended to docs/za-vertical-slice.md after B2's BDN content.
Documents the two-process local recipe (docker + SUT + NBomber) and the
CI job that exercises the same flow on every manual workflow trigger.
The Numbers subsection is a placeholder for the first post-merge CI
run's NBomber report — filled in via a follow-up commit on main, same
pattern as B2's BDN-numbers fill-in.
```

---

## Task 7: Push, update PR #140, trigger workflow, verify

**Step 1: Push all new commits to the existing branch**

```powershell
git push
```

(The branch `feat/postgres-bench-profile` already tracks `origin/feat/postgres-bench-profile`; no `-u` needed.)

**Step 2: Update PR #140 body with the expanded scope**

```powershell
gh pr edit 140 --repo ZeroAlloc-Net/ZeroAlloc.Templates --body "$(cat <<'EOF'
## Summary

Graduates **B2** (BDN Postgres profile) and folds in an expansion: the production app gains an explicit `Database:Provider` switch, and `MyApp.LoadTest` (NBomber) now runs against a real Postgres in CI.

## Changes

### B2 — BDN Postgres profile (original PR scope)

- `WritePipelineBench` cross-products `Method × DbBackend` (Sqlite + Postgres) via `[Params]`. 6 result rows per artifact.
- Postgres profile creates a fresh `bench_<guid8>` database per process via `NpgsqlConnectionStringBuilder`; schema via `EnsureCreated()`.
- EF-stack-strip in `ConfigureServices` clears all `Microsoft.EntityFrameworkCore.*` / `Npgsql.EntityFrameworkCore.*` descriptors before re-adding the DbContext (without this, EF rejects the second provider — see commit `308fbdd` for the diagnosis).
- `.github/workflows/benchmarks.yml` gains a job-level Postgres service container.

### Expansion — production app Postgres support + NBomber-against-Postgres

- **`Database:Provider`** config key (Sqlite | Postgres) dispatches `Program.cs`'s `UseSqlite` vs `UseNpgsql`. Default Sqlite — quickstart unaffected.
- **`Database:SchemaStrategy`** config key (Migrate | EnsureCreated | Skip) replaces the B2-era `Bench:SkipStartupMigrate` bool. Three explicit states; consumed by Program.cs at startup.
- **Npgsql** added as a `<PackageReference>` to production `MyApp.csproj`.
- **`nbomber-postgres-vs`** CI job: services Postgres, builds SUT + LoadTest, starts SUT against Postgres in background, waits for `/healthz`, runs NBomber, uploads reports + SUT log.
- **README** "Swap SQLite → PostgreSQL" recipe updated to the new config-driven path.
- **`docs/za-vertical-slice.md`** gains a "Load testing against Postgres" section.

za-clean replication deferred (B3 backlog entry covers it).

## Design + plan docs

- `docs/plans/2026-05-28-postgres-bench-profile-design.md` + `-plan.md` (B2)
- `docs/plans/2026-05-28-nbomber-postgres-profile-design.md` + `-plan.md` (expansion)

## Test plan

- [x] CI build green (`build`, `build-vs`, aot-publish-smoke, real-run-smoke)
- [x] Manual `Benchmarks (manual)` workflow produces 6 real rows in `bdn-za-vertical-slice-WritePipeline` (verified — run 26592448470)
- [ ] Same workflow produces a populated `nbomber-za-vertical-slice-postgres` artifact (verified by Task 7 of the expansion plan)
- [ ] NBomber numbers folded into `docs/za-vertical-slice.md` "Load testing against Postgres" section in a post-merge commit
EOF
)"
```

**Step 3: Wait for CI build to pass**

```powershell
gh pr checks 140 --watch
```

All checks (`build`, `build-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`, `real-run-smoke`, `real-run-smoke-vs`) must be green before triggering the manual workflow.

**Step 4: Trigger the manual benchmark workflow**

```powershell
gh workflow run benchmarks.yml --ref feat/postgres-bench-profile
```

**Step 5: Wait, then harvest results**

After ~12-15 min (BDN matrix ~6 min, nbomber-postgres-vs ~6-8 min):

```powershell
$run = gh run list --workflow=benchmarks.yml --branch=feat/postgres-bench-profile --limit 1 --json databaseId -q '.[0].databaseId'
gh run download $run -n bdn-za-vertical-slice-WritePipeline -D .bench-artifacts/expanded/bdn
gh run download $run -n nbomber-za-vertical-slice-postgres -D .bench-artifacts/expanded/nbomber
gh run download $run -n nbomber-sut-log -D .bench-artifacts/expanded/sut-log
```

**Step 6: Verify**

- **BDN**: `.bench-artifacts/expanded/bdn/results/MyApp.Benchmarks.WritePipelineBench-report-github.md` still shows 6 real rows. The `Database:SchemaStrategy` rename in Task 3 must not have regressed B2's run.
- **NBomber**: `.bench-artifacts/expanded/nbomber/` contains at least one timestamped report directory with `*.html`, `*.csv`, and/or `*.md` files. The Markdown report shows non-zero RPS, latency percentiles, and a low error rate.
- **SUT log**: `.bench-artifacts/expanded/sut-log/sut.log` shows the SUT started cleanly, logged `Application started`, and handled requests without exceptions during the NBomber run.

If the SUT failed to start or NBomber errored: investigate the SUT log first (most likely cause is a connection-string typo or env-var case mismatch — GHA env keys are case-sensitive in YAML).

**Step 7: Comment on PR #140 with the harvested numbers**

```powershell
gh pr comment 140 --repo ZeroAlloc-Net/ZeroAlloc.Templates --body "$(cat <<'EOF'
## Expansion verified ✅ — NBomber-against-Postgres landed

[paste relevant excerpt from NBomber's Markdown report — RPS, latency percentiles, error rate]

The `nbomber-postgres-vs` CI job (workflow run [ID](URL)) ran the full two-process flow: Postgres service container → SUT against Postgres → `/healthz` ready → NBomber against `localhost:5000`. SUT log shows clean startup and no exceptions during the run.

BDN matrix rows still match B2's verified numbers (the `Database:SchemaStrategy` rename in commit `[task3-sha]` preserved bench semantics — Sqlite branch sets `Migrate`, Postgres branch sets `Skip`).

Ready to merge.
EOF
)"
```

**Step 8: Wait for user merge**

Once merged, follow up with a `docs:` commit on `main` that pastes the actual NBomber numbers into `docs/za-vertical-slice.md`'s placeholder. Same flow as B2's BDN-numbers fill-in.

---

## Out of scope

- **Integration test conversion to Postgres** (per Q2 = A): `MyApp.IntegrationTests` stays on in-memory SQLite. Separate workstream.
- **Unit test conversion to Postgres**: also stays on in-memory SQLite. Handler unit tests verify EF behavior against the runtime model, which is the same for both providers.
- **za-clean NBomber+Postgres replication**: filed as a follow-up backlog item (B4 or rolling under B3).
- **Postgres-typed migrations for production deployments**: adopters scaffold their own via `dotnet ef migrations add ... --output-dir Persistence/Migrations.Postgres`. The template ships Sqlite-typed migrations only.
