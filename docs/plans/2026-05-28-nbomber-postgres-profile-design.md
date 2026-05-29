# NBomber-against-Postgres profile for za-vertical-slice — design

**Status:** approved 2026-05-28
**Folded into:** PR #140 (feat/postgres-bench-profile branch — same PR as B2's BDN-Postgres profile)
**Backlog item:** extension of B2; "test against a real DB" framing

## Goal

Make `MyApp.LoadTest` exercise the SUT against a real Postgres instance instead of in-memory SQLite. The existing NBomber profile was capped at ~470 RPS by SQLite's single-process file lock — not a meaningful production signal. Switching the SUT to Postgres surfaces real-DB ceiling and removes the artifact-of-the-test-harness lock.

## Locked scope (Q&A)

- **Q1 — Where does NBomber-against-Postgres live?** **B**: CI workflow leg + local recipe.
- **Q2 — How wide is the "real DB" net cast?** **A**: NBomber only. Integration tests, unit tests, BDN (`[Params]` cross-product stays) untouched.
- **DB target:** Postgres only. Sqlite stays as the template's zero-setup quickstart default; nothing else.

## Approach

### 1. Production app DB choice mechanism

Explicit `Database:Provider` config key, not connection-string sniffing.

```csharp
var provider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        opts.UseNpgsql(connectionString);
    }
    else
    {
        opts.UseSqlite(connectionString);
    }
    opts.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
```

Alternative rejected: connection-string-prefix sniffing (`Host=` → Npgsql). Explicit > magic; future-proof for SqlServer/MySQL; loud failures when misconfigured.

**Package addition:** `Npgsql.EntityFrameworkCore.PostgreSQL` already in `Directory.Packages.props` (from B2's bench wire-up). Add `<PackageReference>` to **production** `MyApp.csproj`. ~2 MB on disk, only loaded when Postgres is selected.

**Quickstart unaffected:** default `Data Source=app.db` → UseSqlite → zero-setup `dotnet run`.

**Failure mode:** if `Database:Provider=Postgres` is set without a matching `ConnectionStrings:Default`, the default `"Data Source=app.db"` flows into `UseNpgsql` → Npgsql fails fast with "Missing Host=" or similar. Clear error, adopter fixes config.

### 2. Schema strategy

Consolidate the B2-era `Bench:SkipStartupMigrate` flag into a single richer `Database:SchemaStrategy` key with three states:

- **`Migrate`** *(default)* — production behavior. `MigrateAsync()` on startup. Used by SQLite quickstart + production Postgres deployments that have scaffolded their own Postgres-typed migrations.
- **`EnsureCreated`** — runtime-model schema creation. Used by **NBomber-against-Postgres** and ad-hoc Postgres experimentation. Documented limitation: bypasses migrations history, so production-bound Postgres adopters must scaffold proper migrations and switch back to `Migrate`.
- **`Skip`** — startup does nothing. Used by `WritePipelineBench`'s Postgres branch, which owns schema creation in `[GlobalSetup]`. Replaces `Bench:SkipStartupMigrate=true`.

`Program.cs`:

```csharp
var schemaStrategy = builder.Configuration.GetValue<string>("Database:SchemaStrategy") ?? "Migrate";

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

**Bench code update** (`WritePipelineBench.Setup`): replace `["Bench:SkipStartupMigrate"] = skipMigrate ? "true" : "false"` with `["Database:SchemaStrategy"] = skipMigrate ? "Skip" : "Migrate"`. The B2 flag was ~24 hours old with one consumer (the bench code we own); renaming costs nothing.

**Why consolidate** over keeping `Bench:SkipStartupMigrate` + adding `Schema:UseEnsureCreated`: one config key, three explicit states, no flag-interaction matrix to reason about.

### 3. NBomber harness

`MyApp.LoadTest/Program.cs` stays as-is — already takes `args[0]` as the base URL. Two execution paths share the scenario code, differ only in how the SUT is spun up.

**Local recipe** (documented in `docs/za-vertical-slice.md` under a new "Load testing against Postgres" section, plus a top-of-file comment in `MyApp.LoadTest/Program.cs`):

```bash
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
  --name myapp-load-pg postgres:17

Database__Provider=Postgres \
Database__SchemaStrategy=EnsureCreated \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load" \
dotnet run -c Release --project src/MyApp &

until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
dotnet run -c Release --project benchmarks/MyApp.LoadTest

kill %1; docker stop myapp-load-pg
```

**CI workflow leg** — new top-level job `nbomber-postgres-vs` in `.github/workflows/benchmarks.yml`, alongside (not under) the existing `benchmark` matrix job. Steps differ enough that nesting into the matrix would require ugly `if:` guards.

```yaml
nbomber-postgres-vs:
  name: za-vertical-slice / NBomber-Postgres
  runs-on: ubuntu-latest
  timeout-minutes: 30
  # NBomber against a real Postgres. Distinct from the BDN matrix above
  # because it spins up the SUT as a separate process. Postgres is the
  # only sensible load-test target — in-memory SQLite's single-process lock
  # caps at ~470 RPS. See docs/za-vertical-slice.md §"Load testing against
  # Postgres".
  services:
    postgres:
      image: postgres:17
      env:
        POSTGRES_PASSWORD: postgres
        POSTGRES_DB: myapp_load
      ports: ["5432:5432"]
      options: >-
        --health-cmd pg_isready --health-interval 5s
        --health-timeout 3s --health-retries 5
  steps:
    - uses: actions/checkout@v6
    - uses: actions/setup-dotnet@v5
      with: { dotnet-version: 10.0.x }
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
      run: nohup dotnet run -c Release --no-build --project src/MyApp > sut.log 2>&1 &
    - name: Wait for /healthz
      run: |
        for i in {1..60}; do
          if curl -fs http://localhost:5000/healthz; then break; fi
          sleep 1
        done
    - name: Run NBomber
      working-directory: content/za-vertical-slice
      run: dotnet run -c Release --no-build --project benchmarks/MyApp.LoadTest -- http://localhost:5000
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

**Top-of-file comment in `benchmarks.yml`** updates to acknowledge NBomber is no longer universally skipped — the old skip reason ("fragile on shared CI runners") is partially mitigated because SUT runs on the same runner as NBomber (no cross-host network) and the scenario is throughput-measuring, not max-VU-stressing.

**Trade-off considered:** keeping NBomber out of CI entirely — rejected per Q1 = B. If the CI job becomes a frequent retry-magnet, it gets demoted to manual-only in a follow-up.

### 4. Documentation

- **`docs/za-vertical-slice.md`** — new "Load testing against Postgres" section with the docker-run recipe + placeholder for numbers (filled in after first post-merge CI run, same pattern as B2's BDN numbers).
- **`content/za-vertical-slice/README.md`** — the existing "Swap SQLite → PostgreSQL" recipe (line ~137) updates to the new `Database:Provider` + `ConnectionStrings:Default` mechanism + the `Database:SchemaStrategy` knob.
- **`docs/backlog.md`** — file B4 (NBomber-Postgres replication to za-clean, mirroring B3's structure) or roll into an "open observations" addendum to the just-shipped B2 entry.

## Risks / out-of-scope

- **Numbers depend on runner I/O variance.** Localhost Postgres on a GHA runner has noisier latency than a tuned host. Trend over time matters more than absolute numbers from one run.
- **EnsureCreated drift.** If entity classes evolve, the runtime-model schema may diverge from what scaffolded Postgres migrations would produce. Acceptable for load-testing; documented as production-bound caveat.
- **za-clean replication deferred** to a follow-up backlog item, mirroring B3's structure. Same rationale: graduate when adopter asks.
- **Production-grade migrations** for Postgres are explicitly NOT shipped. Adopters going to production Postgres scaffold their own.

## Graduation

Closed when:
1. PR #140 (expanded) merges with all of: production-code DB switch, schema-strategy consolidation, NBomber recipe + CI leg, docs.
2. The `Benchmarks (manual)` workflow run on `main` produces NBomber numbers under the `nbomber-za-vertical-slice-postgres` artifact.
3. `docs/za-vertical-slice.md` "Load testing against Postgres" section gets the numbers + interpretation paragraph in a post-merge commit.
