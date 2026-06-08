# Bench Capacity Recipe + Reframe — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship a runnable single-laptop capacity-bench recipe + documentation that distinguishes "regression net" (current CI) from "capacity" (decoupled local recipe) — no CI infrastructure changes.

**Architecture:** Two new markdown docs under `docs/benchmarks/` + minimal pointer edits in `.github/workflows/benchmarks.yml` and both template READMEs. The implementation is docs-only; the existing LoadTest binary already supports the decoupled pattern (`baseUrl` positional arg + `LOADTEST_TARGET_RPS` / `LOADTEST_DURATION_S` env vars). One pre-merge verification run on the maintainer's laptop proves the recipe works end-to-end.

**Tech Stack:** Markdown only. References to Docker (`--cpuset-cpus`), Windows process affinity, Linux `taskset`, and the existing `MyApp.LoadTest` binary.

**Design reference:** [docs/plans/2026-06-08-bench-capacity-recipe-design.md](2026-06-08-bench-capacity-recipe-design.md) (commit `27932a1`)
**Branch:** `docs/170-bench-capacity-recipe` off `main` at the post-#187 commit.

---

## Preflight

This is a docs PR. No SDK pin dance needed for any task except Task 3 (recipe verification, which runs `dotnet run`).

All paths below are relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.

The branch already exists locally (`docs/170-bench-capacity-recipe`) with the design doc committed at `27932a1`. Confirm before starting:

```powershell
git status
git log --oneline -3
```

Expected: clean tree on `docs/170-bench-capacity-recipe`, top commit `27932a1 docs(design): bench capacity recipe + regression-net framing (#170)`.

---

## Task 1: Write `docs/benchmarks/README.md`

**Goal:** Top-level entry point for the `docs/benchmarks/` folder. Frames the existing report files and explains the regression-net vs capacity distinction.

**Files:**
- Create: `docs/benchmarks/README.md`

**Step 1: Inventory existing reports**

The new README must index every existing report. Run:

```powershell
Get-ChildItem docs/benchmarks -Filter "*.md" | Select-Object -ExpandProperty Name
```

Expected (current state — verify before writing):
- `2026-06-04-flat-read-model-alloc.md`
- `2026-06-04-za-clean-sec-context-alloc.md`
- `2026-06-04-za-vertical-slice-sec-context-alloc.md`
- `2026-06-05-nbomber-ceiling-sweep.md`
- `2026-06-07-189-mediator-dispatch-clean.md`
- `2026-06-07-189-mediator-dispatch-vs.md`
- `2026-06-07-189-read-pipeline-clean.md`
- `2026-06-07-189-read-pipeline-vs.md`

If the list differs, adjust the index section accordingly.

**Step 2: Write the README**

Create `docs/benchmarks/README.md` with this exact content:

```markdown
# Benchmark documentation

This folder holds benchmark reports — BenchmarkDotNet `.md` exports and NBomber run summaries — plus the runbooks for producing them.

## What the benches measure

The project has three categories of benchmark, each answering a different question.

### 1. BDN micro-benches (per-call CPU + allocation)

Single-threaded, deterministic. Run in-process against in-memory backends. Measure the cost of one isolated operation (a mediator dispatch, a security-context read, an ORM materialization) with `MemoryDiagnoser` reporting allocations per call.

**Use to answer:** "How much does this operation cost on the hot path?" — alloc bytes, ns per op.
**Examples in this folder:** `2026-06-04-za-clean-sec-context-alloc.md`, `2026-06-04-flat-read-model-alloc.md`, `2026-06-07-189-mediator-dispatch-*.md`.

### 2. HTTP-level BDN (single request through the full stack)

BDN benchmarks that boot a `WebApplicationFactory<Program>` SUT and fire one HTTP request per iteration. Kestrel, JWT, JSON serialization, mediator, repo, response — all included. Single-request semantics (no concurrency).

**Use to answer:** "What's the per-request CPU + alloc cost end-to-end, ignoring concurrency effects?"
**Examples in this folder:** `2026-06-07-189-read-pipeline-*.md`.

### 3. NBomber load tests (concurrent throughput + latency under load)

Open-model load injection. NBomber pushes requests at a target arrival rate while measuring actual sustained RPS, failure rate, and latency percentiles.

**Use to answer:** "What's the SUT's sustainable throughput and tail latency under concurrent load?"
**Examples in this folder:** `2026-06-05-nbomber-ceiling-sweep.md`.

## Regression-net vs capacity — the important distinction

NBomber results in this repo come from **two different setups** that measure different things. Conflating them produces wrong claims.

### Regression net (CI co-located)

`.github/workflows/benchmarks.yml` runs the load generator, SUT, and Postgres on the **same** GitHub-hosted runner (~2-4 vCPUs). NBomber, Kestrel, and Postgres fight for the same cores.

Empirical proof from 2026-06-03 CI at 5k target RPS: actual RPS = 4,312, p50 = 188.8 ms, **min = 0.55 ms**. The 0.55 ms minimum proves the SUT can serve uncontended requests in half a millisecond — the 188 ms p50 is queueing because the load generator is starving the SUT for CPU.

**Use these numbers for:** detecting regressions across PRs on a consistent (if tainted) hardware profile.
**Do NOT use these numbers for:** capacity planning, README claims, marketing material, or any "this template handles X RPS" statement.

The 4,312 RPS plateau you see in CI is the **open-model NBomber injector's per-machine cap**, not SUT capacity. Verified independently — see `2026-06-05-nbomber-ceiling-sweep.md`.

### Capacity (decoupled, local recipe)

The `capacity-recipe.md` in this folder describes how to run NBomber on a single laptop with Docker CPU pinning that isolates the load generator from the SUT and Postgres. This measures actual SUT capacity, not co-location artifacts.

**Use these numbers for:** README RPS claims, capacity planning, investigation of perf issues that surface only under concurrent load (e.g. [#189](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/189)).

The 2026-06-05 sweep on an i9-12900HK found za-clean's actual ceiling at ~6,900 RPS sustainable — **~1.6× higher than CI ever reports**. Per-template numbers and the full method live in `2026-06-05-nbomber-ceiling-sweep.md`.

## When to update RPS claims in template READMEs

Any RPS claim in `content/za-clean/README.md` or `content/za-vertical-slice/README.md` must be backed by a **capacity** measurement, not a regression-net measurement. Process:

1. Run the recipe in `capacity-recipe.md` on a quiet machine.
2. Save the NBomber report under `docs/benchmarks/<YYYY-MM-DD>-<topic>.md`.
3. Open a PR that updates the README number AND adds the report link to the index below.

## Report index

| Date | File | Category | What it measured |
|---|---|---|---|
| 2026-06-04 | [za-clean-sec-context-alloc.md](2026-06-04-za-clean-sec-context-alloc.md) | BDN micro | #172 — `ClaimsPrincipalSecurityContext` per-call alloc post zero-alloc rewrite |
| 2026-06-04 | [za-vertical-slice-sec-context-alloc.md](2026-06-04-za-vertical-slice-sec-context-alloc.md) | BDN micro | #172 mirror for vs template |
| 2026-06-04 | [flat-read-model-alloc.md](2026-06-04-flat-read-model-alloc.md) | BDN micro | #173 — `ReadHotPathBench` post flat read model |
| 2026-06-05 | [nbomber-ceiling-sweep.md](2026-06-05-nbomber-ceiling-sweep.md) | NBomber capacity | Laptop rate-sweep finding actual SUT ceilings (~6.9k clean, ~4.3k vs) |
| 2026-06-07 | [189-mediator-dispatch-clean.md](2026-06-07-189-mediator-dispatch-clean.md) | BDN micro | #189 — in-process mediator dispatch, za-clean |
| 2026-06-07 | [189-mediator-dispatch-vs.md](2026-06-07-189-mediator-dispatch-vs.md) | BDN micro | #189 mirror for vs |
| 2026-06-07 | [189-read-pipeline-clean.md](2026-06-07-189-read-pipeline-clean.md) | HTTP-level BDN | #189 — full HTTP-stack single-request bench, za-clean |
| 2026-06-07 | [189-read-pipeline-vs.md](2026-06-07-189-read-pipeline-vs.md) | HTTP-level BDN | #189 mirror for vs |
```

**Step 3: Sanity-check the file**

```powershell
Get-Content docs/benchmarks/README.md | Measure-Object -Line
```

Expected: ~80 lines (within ±15).

```powershell
git diff --stat docs/benchmarks/README.md
```

Expected: a new file with all lines added.

**Step 4: Commit**

```powershell
git add docs/benchmarks/README.md
git commit -m @'
docs(bench): top-level README for docs/benchmarks/ (#170)

Frames the three bench categories (BDN micro / HTTP-level BDN / NBomber)
and introduces the regression-net vs capacity distinction. Indexes
every existing report file with date, category, and what it measured.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

The `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` line is the established attribution pattern in this repo — see `git log -10 --format=%B` for prior commits. Don't omit.

---

## Task 2: Write `docs/benchmarks/capacity-recipe.md`

**Goal:** Runnable single-laptop capacity-bench recipe. Must work end-to-end on a Windows or Linux laptop with Docker.

**Files:**
- Create: `docs/benchmarks/capacity-recipe.md`

**Step 1: Write the recipe**

Create `docs/benchmarks/capacity-recipe.md` with this exact content:

````markdown
# Capacity bench recipe (decoupled NBomber)

Runs the NBomber load test against the SUT with the load generator, SUT, and Postgres on **isolated CPU cores** — eliminates the co-location contention that pins CI numbers at the injector cap. The result is actual SUT capacity rather than a regression-net number.

> When to use this: capacity planning, README RPS claims, investigation of perf issues that surface only under concurrent load. For day-to-day "did this PR regress?" CI is fine. See [README.md](README.md) for the distinction.

## Hardware floor

- **8+ physical cores recommended.** The recipe pins Postgres to 2 cores and the SUT to 4 cores; NBomber needs the remaining cores plus the OS. On a 4-core machine you can still run it but the numbers will reflect contention, not capacity.
- **16+ GB RAM.** The SUT seeds 1000 orders during NBomber init; Postgres holds them in memory at this size.
- **Quiet machine.** Close other workloads — capacity measurements are sensitive to background CPU.
- **Docker Desktop (Windows / macOS) or Docker Engine (Linux).** Both work; the `docker run --cpuset-cpus="..."` syntax is identical.

## Recipe

All commands assume PowerShell on Windows or bash on Linux. Working dir: the repo root.

### Step 1: Start Postgres pinned to cores 0–1

```powershell
$pgName = "pg-capacity-$(Get-Random)"
docker run --rm -d --name $pgName --cpuset-cpus="0-1" `
  -e POSTGRES_PASSWORD=postgres `
  -e POSTGRES_DB=myapp_load `
  -p 5432:5432 `
  postgres:17 `
  -c max_connections=2000

# Wait for readiness
Start-Sleep -Seconds 5
docker exec $pgName pg_isready -U postgres
```

Linux equivalent (bash):

```bash
PG_NAME="pg-capacity-$RANDOM"
docker run --rm -d --name "$PG_NAME" --cpuset-cpus="0-1" \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=myapp_load \
  -p 5432:5432 \
  postgres:17 \
  -c max_connections=2000
sleep 5
docker exec "$PG_NAME" pg_isready -U postgres
```

`max_connections=2000` matches the LoadTest's `SocketsHttpHandler` connection cap. Lower than 2000 → Postgres rejects connections under load.

### Step 2: Build SUT in Release

Pick the template you want to measure. For za-clean:

```powershell
dotnet build content/za-clean/src/MyApp.Api -c Release
dotnet build content/za-clean/benchmarks/MyApp.LoadTest -c Release
```

For za-vertical-slice:

```powershell
dotnet build content/za-vertical-slice/src/MyApp -c Release
dotnet build content/za-vertical-slice/benchmarks/MyApp.LoadTest -c Release
```

### Step 3: Start the SUT pinned to cores 2–5

Pick the OS that matches your machine. The SUT must end up running on cores 2-5 (a 4-core slice that doesn't overlap with Postgres on 0-1 or NBomber on 6+).

#### Linux — `taskset`

```bash
# za-clean
ASPNETCORE_URLS="http://localhost:5000" \
Database__Provider=Postgres \
Database__SchemaStrategy=EmbeddedScript \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
Shipping__UseStub=true \
taskset -c 2-5 dotnet run -c Release --no-build \
  --project content/za-clean/src/MyApp.Api &
SUT_PID=$!
```

#### Windows — process affinity via PowerShell

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5000"
$env:Database__Provider = "Postgres"
$env:Database__SchemaStrategy = "EmbeddedScript"
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500"
$env:Shipping__UseStub = "true"

# Start the SUT; capture the process so we can pin its affinity after launch.
$sut = Start-Process dotnet `
  -ArgumentList @("run","-c","Release","--no-build","--project","content/za-clean/src/MyApp.Api") `
  -PassThru
# 0x3C = cores 2,3,4,5 (bitmask 0b111100)
$sut.ProcessorAffinity = [IntPtr]::new(0x3C)
```

`Shipping__UseStub=true` swaps the typed shipping client for an in-memory stub. Without it, every POST /orders during NBomber init hits a DNS failure on `shipping.example` and seeding aborts. This env var only exists on za-clean — vs has no outbound shipping call.

#### Cross-platform — Docker (alternative)

If a Dockerfile exists for the template, `docker run --cpuset-cpus="2-5" myapp:latest` is the cleanest variant. As of this writing **no Dockerfile ships with either template** — adding one is separate work. Use the OS-specific commands above.

### Step 4: Wait for `/healthz`

```powershell
1..30 | ForEach-Object {
    try {
        $r = Invoke-WebRequest http://localhost:5000/healthz -UseBasicParsing -TimeoutSec 1
        if ($r.StatusCode -eq 200) { Write-Host "SUT healthy after $_ s"; break }
    } catch { Start-Sleep -Seconds 1 }
}
```

Linux:

```bash
for i in {1..30}; do
  if curl -fsS http://localhost:5000/healthz > /dev/null; then
    echo "SUT healthy after ${i}s"
    break
  fi
  sleep 1
done
```

### Step 5: Run NBomber on the host (NO CPU pinning)

The host scheduler will naturally distribute the load generator across the cores not pinned by Postgres or the SUT. On a 16-core box, cores 6–15 (10 cores) absorb NBomber and the OS.

For a single rate measurement at 5k target:

```powershell
$env:LOADTEST_TARGET_RPS = "5000"
$env:LOADTEST_DURATION_S = "30"
dotnet run -c Release --no-build `
  --project content/za-clean/benchmarks/MyApp.LoadTest `
  -- http://localhost:5000
```

For a sweep across 5k / 8k / 12k / 16k targets — the recommended way to find the actual ceiling:

```powershell
foreach ($rps in @(5000, 8000, 12000, 16000)) {
    $env:LOADTEST_TARGET_RPS = "$rps"
    $env:LOADTEST_DURATION_S = "30"
    Write-Host "=== Target $rps RPS ==="
    dotnet run -c Release --no-build `
      --project content/za-clean/benchmarks/MyApp.LoadTest `
      -- http://localhost:5000
    # Truncate seeded data between runs so each leg starts clean
    docker exec $pgName psql -U postgres -d myapp_load `
      -c "TRUNCATE TABLE order_lines, orders RESTART IDENTITY CASCADE;" 2>$null
}
```

The NBomber report markdown writes to `content/za-clean/benchmarks/MyApp.LoadTest/reports/` (or `content/za-vertical-slice/...` for the vs template). Copy interesting ones to `docs/benchmarks/<YYYY-MM-DD>-<topic>.md`.

### Step 6: Stop the SUT and Postgres

```powershell
# Windows
Stop-Process -Id $sut.Id -Force
docker stop $pgName
```

Linux:

```bash
kill $SUT_PID
docker stop "$PG_NAME"
```

## What to expect (reference numbers)

From the 2026-06-05 sweep on an i9-12900HK (i.e. 16 cores, the recipe's sweet spot):

| Template | Target | Actual RPS | Fail% | p50 | p95 | p99 |
|---|---:|---:|---:|---:|---:|---:|
| za-clean | 5,000 | 4,312 | 0% | 7 ms | 16 ms | 29 ms |
| **za-clean** | **8,000** | **6,900** | **0%** | **17 ms** | **122 ms** | **216 ms** |
| za-clean | 12,000 | 6,566 | 37% | 7,872 ms | 13 s | 14 s |
| za-vs | 5,000 | 4,312 | 0% | 21 ms | 252 ms | 295 ms |
| za-vs | 6,000 | 2,837 | 45% | 11,870 ms | 18 s | — |

za-clean sustainable ceiling on that hardware: **~6,900 RPS**. za-vs is much sharper-saturating (see [#189](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/189)).

If your results are wildly different on similar hardware, suspect:
- Background workload — close other apps.
- Docker resource limits — Docker Desktop's resource pane on Windows/macOS may cap container CPU.
- Network: localhost isn't free. On Windows expect ~0.1ms localhost roundtrip overhead per request.
- Thermal — a hot laptop throttles; let it cool between sweep legs.

## Caveats — honest limits of this recipe

- **Single-machine recipes can't eliminate L3-cache, memory-bandwidth, and GC contention** between the SUT process and the NBomber process. CPU pinning is necessary but not sufficient for true isolation.
- **The injector itself has an open-model cap.** At 5k target on a 16-core box, NBomber tops out at exactly 4,312.5 actual RPS on both templates — that's NBomber's per-machine injector ceiling, not SUT capacity. **The capacity number lives at target rates _above_ the injector cap**, where NBomber is no longer the bottleneck. See `2026-06-05-nbomber-ceiling-sweep.md` for the empirical evidence.

For gold-standard capacity numbers, use the two-machine variant below.

## Two-machine LAN variant (appendix)

When the SUT and load generator run on **different physical machines** on the same LAN, machine-level contention disappears entirely. Recipe:

### Host A (SUT + Postgres)

```bash
# Postgres without CPU pinning — it has the machine to itself
docker run --rm -d --name pg-capacity \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
  -p 5432:5432 postgres:17 -c max_connections=2000

# SUT bound to all interfaces so host B can reach it
ASPNETCORE_URLS="http://0.0.0.0:5000" \
Database__Provider=Postgres \
Database__SchemaStrategy=EmbeddedScript \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
Shipping__UseStub=true \
dotnet run -c Release --project content/za-clean/src/MyApp.Api
```

### Host B (NBomber)

```bash
LOADTEST_TARGET_RPS=10000 LOADTEST_DURATION_S=30 \
dotnet run -c Release --project content/za-clean/benchmarks/MyApp.LoadTest \
  -- http://hostA.local:5000
```

Replace `hostA.local` with host A's hostname or IP.

### Caveats — two-machine variant

- **LAN RTT contributes directly to per-request mean latency.** Only meaningful on a wired LAN with sub-1ms RTT. WiFi-to-WiFi typically 3–15 ms RTT, which swallows the signal.
- **Bandwidth at high RPS** — at 10k+ RPS with ~500-byte responses, you're pushing ~5 MB/s of HTTP. Gigabit Ethernet handles it easily; saturated WiFi may not.
- **Host A's CPU is fully available to SUT + Postgres.** No pinning needed.
````

**Step 2: Sanity-check the file**

```powershell
Get-Content docs/benchmarks/capacity-recipe.md | Measure-Object -Line
```

Expected: ~200 lines (the design said "~150" but the cross-OS variants push it higher; that's fine).

**Step 3: Commit**

```powershell
git add docs/benchmarks/capacity-recipe.md
git commit -m @'
docs(bench): single-laptop capacity recipe with Docker CPU pinning (#170)

Step-by-step recipe for running NBomber decoupled from the SUT on a
single laptop. Postgres pinned to cores 0-1, SUT to cores 2-5, NBomber
on remaining cores via host scheduler. Includes Linux (taskset) and
Windows (PowerShell ProcessorAffinity) variants, plus a two-machine LAN
appendix.

Reference numbers from the 2026-06-05 sweep included so future runs can
sanity-check their setup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 3: Verify the recipe works end-to-end (verification gate, no commit)

**Goal:** Prove the recipe works on the maintainer's laptop before shipping it. A docs PR claiming "do X" must demonstrate X actually works. Single run at 5k target on one template is enough — we're verifying the recipe, not measuring perf.

**Files:** none. This task is verification only.

**Step 1: Confirm Docker Desktop is running**

```powershell
docker ps
```

If the command fails or shows the daemon isn't running, start Docker Desktop manually and wait until it's ready. **Do NOT proceed otherwise** — the recipe assumes Docker is available.

**Step 2: SDK pin dance if needed**

If `global.json` pins `10.0.300` and the local SDK is `10.0.108` / `10.0.204`:

```powershell
# Edit global.json to use { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
# DO NOT COMMIT THIS CHANGE.
```

Restore via `git restore global.json` after the run.

**Step 3: Run the recipe at 5k target on za-clean**

Follow the recipe in `docs/benchmarks/capacity-recipe.md` step-by-step. If any step is unclear or fails, that's a recipe bug — fix the recipe (loop back to Task 2 step 1) and re-run.

Expected outcome at 5k target on the maintainer's i9-12900HK:
- NBomber report writes to `content/za-clean/benchmarks/MyApp.LoadTest/reports/<timestamp>/`.
- Report shows actual RPS in the 4,000-5,000 range (exact number machine-dependent).
- Failure rate < 5%.
- The recipe steps complete without manual intervention beyond what's documented.

**Step 4: Check the report**

Open the generated NBomber `.md` report. Confirm:
- Header shows scenario `read_order_by_id`.
- ok count is reported.
- p50/p95/p99 latency are reported.
- The Postgres / SUT / NBomber processes all started and stopped cleanly.

**Step 5: If anything was unclear or broken in the recipe**

Loop back to Task 2 Step 1: amend `capacity-recipe.md` to fix the issue, then re-commit (use `--amend` if the fix is small, or a new `docs(bench): refine recipe` commit if substantive).

**Step 6: Restore environment**

```powershell
# If global.json was relaxed:
git restore global.json
git status
# Should show no modifications (only untracked .bench-* dirs).
```

```powershell
# Confirm Postgres container is stopped
docker ps
# Confirm SUT process is not running
Get-Process dotnet -ErrorAction SilentlyContinue
```

If the SUT is still running, kill it before continuing.

**Step 7: No commit**

This task is a verification gate, not a code change. If you got here, the recipe works.

---

## Task 4: Update workflow comments + template README footnotes

**Goal:** Point readers from the existing CI workflow and template READMEs to the new docs.

**Files:**
- Modify: `.github/workflows/benchmarks.yml` (two comment blocks)
- Modify: `content/za-clean/README.md` (one footnote near the RPS claim at line ~69-75)
- Modify: `content/za-vertical-slice/README.md` (one footnote near the RPS claim at line ~45)

**Step 1: Update `benchmarks.yml` comments**

Find the existing comment in the `nbomber-postgres-vs` job that says:

```
# Open-model injection (Simulation.Inject), bounded for the same reason as
# nbomber-postgres-clean: generator + SUT + Postgres share this runner's
# few vCPUs, so the code's 10k/s default would just starve the SUT. 5000/s
# overshoots the historical closed-loop baseline enough to expose the
# post-tuning ceiling. For a true ceiling, run the generator off-box and
# raise LOADTEST_TARGET_RPS to 10000.
```

Replace the last sentence (`For a true ceiling…`) with:

```
# This is a regression net, not a capacity measurement — see
# docs/benchmarks/README.md and docs/benchmarks/capacity-recipe.md for
# the decoupled local recipe that produces real capacity numbers.
```

Do the same edit in the `nbomber-postgres-clean` job's analogous comment block (around line 321-328).

**Step 2: Update `content/za-clean/README.md`**

Find the RPS claim. Per the earlier survey, around line 69-75:

```
The NBomber load test scenario (read RPS, open-model 5,000-RPS inject for 30s + 10s ramp against real Kestrel, Postgres backend):
| Mean | p50 | p95 | p99 | RPS | Notes |
…
```

Immediately after the table (before the next prose paragraph), insert:

```
> ⚠️ **Regression-net numbers.** The CI workflow that produces these numbers
> co-locates NBomber, Kestrel, and Postgres on the same GitHub runner — the
> "4,312 RPS" plateau is the NBomber injector cap on that hardware, not the
> SUT's actual capacity. See [docs/benchmarks/README.md](../../docs/benchmarks/README.md)
> for the distinction; [docs/benchmarks/capacity-recipe.md](../../docs/benchmarks/capacity-recipe.md)
> documents the decoupled recipe that measures real capacity. Actual ceiling
> on a 16-core i9 is ~6,900 RPS — see
> [docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md](../../docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md).
```

**Step 3: Update `content/za-vertical-slice/README.md`**

Around line 45 (the line that says `NBomber-Postgres at 5,000-RPS open-model inject sustains 4,312 RPS / 0 failures …`), append a similar disclaimer footnote at the end of that paragraph:

```
> ⚠️ **Regression-net numbers, not capacity.** The CI bench co-locates load
> generator + SUT + Postgres on the same runner. See
> [docs/benchmarks/README.md](../../docs/benchmarks/README.md) for context and
> [docs/benchmarks/capacity-recipe.md](../../docs/benchmarks/capacity-recipe.md)
> for the decoupled recipe.
```

Match the per-template README's existing prose style — adjust wording slightly if it reads awkwardly in context.

**Step 4: Build the templates to confirm READMEs aren't part of any embedded resource**

```powershell
dotnet build content/za-clean -c Release -v minimal 2>&1 | Select-String "error" -SimpleMatch | Select-Object -First 3
dotnet build content/za-vertical-slice -c Release -v minimal 2>&1 | Select-String "error" -SimpleMatch | Select-Object -First 3
```

Expected: no errors. The READMEs are documentation, not source — these builds should remain clean.

**Step 5: Spot-check rendering**

Open the modified files locally:

```powershell
Get-Content content/za-clean/README.md -TotalCount 100 | Select-Object -Last 40
Get-Content content/za-vertical-slice/README.md -TotalCount 60 | Select-Object -Last 20
Get-Content .github/workflows/benchmarks.yml | Select-String -Pattern "capacity-recipe" -Context 2
```

Confirm the inserted text reads cleanly in context. Adjust if surrounded text needs minor wording tweaks.

**Step 6: Commit**

```powershell
git add `
  .github/workflows/benchmarks.yml `
  content/za-clean/README.md `
  content/za-vertical-slice/README.md
git commit -m @'
docs(bench): point CI comments + template READMEs at new bench docs (#170)

The two NBomber CI jobs' inline comments now point at docs/benchmarks/
for the regression-net-vs-capacity distinction and the local capacity
recipe. Both template READMEs gain a footnote near their RPS claim
flagging the number as a regression-net measurement and linking to the
capacity recipe and the 2026-06-05 sweep.

Documented numbers themselves are unchanged — replacing them is
separate work that requires a fresh capacity-recipe run.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: Push, PR, admin-merge

**Goal:** Land the branch. release-please should NOT bump version (`docs:` prefix maps to no bump per the repo's config).

**Files:** none. Git/gh operations only.

**Step 1: Push the branch**

```powershell
git push -u origin docs/170-bench-capacity-recipe
```

**Step 2: Open the PR**

```powershell
gh pr create --title "docs(bench): decoupled-generator capacity recipe + framing (#170)" --body @'
## Summary

Closes #170. Ships two new docs under `docs/benchmarks/` that solve the issue's two acceptance criteria:

- **Decoupled-generator benchmark profile exists and is documented** — `docs/benchmarks/capacity-recipe.md` is the runnable single-laptop recipe with Docker CPU pinning (Postgres on cores 0-1, SUT on 2-5, NBomber on host cores 6+). Includes Linux (`taskset`) and Windows (PowerShell `ProcessorAffinity`) variants, plus a two-machine LAN appendix.
- **Docs distinguish "regression net" (co-located) from "capacity" (decoupled)** — `docs/benchmarks/README.md` is the top-level entry point with the framing, references the 2026-06-05 sweep as empirical evidence of the CI co-location problem, and indexes every existing report file.

Plus minimal pointer edits:
- `.github/workflows/benchmarks.yml` — two inline comment updates pointing readers at the new docs (no behavior change).
- `content/za-clean/README.md` + `content/za-vertical-slice/README.md` — footnote near each RPS claim flagging the number as regression-net.

## What this PR is NOT

- **Not a CI infrastructure change.** Existing co-located NBomber jobs stay exactly as they are — they're now framed as a regression net, which is what they actually measure.
- **Not a benchmark code change.** The existing `MyApp.LoadTest` binary already supports the decoupled pattern (`baseUrl` positional arg, `LOADTEST_TARGET_RPS` env var).
- **Not an RPS number update in template READMEs.** The footnote disclaimer is the cheap fix; replacing the actual numbers is follow-up work after a fresh capacity-recipe run.

## Verification

The recipe in `capacity-recipe.md` was run end-to-end on the maintainer's i9-12900HK laptop (Task 3 of the implementation plan) before merge. The run produced an NBomber report with the expected shape.

## Approach

Approach D from the brainstorm (local-only recipe + documentation, no CI infrastructure). Design at `docs/plans/2026-06-08-bench-capacity-recipe-design.md`. Rejected alternatives: self-hosted runners (no budget), paid GHA larger runners (no budget), cloudflared-tunnel coordination (tunnel latency dominates the measurement).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

Capture the PR number from the command output.

**Step 3: Wait for CI**

```powershell
gh pr checks --watch
```

Expected: all checks pass. This is a docs PR with no code changes — `build`, `build-vs`, `real-run-smoke`, `aot-publish-smoke` should all be unaffected.

If a check fails, STOP and report. Docs PR shouldn't break the build; if it does, something's wrong.

**Step 4: Admin-merge with `docs:` squash**

```powershell
$prNumber = gh pr view --json number -q .number
gh pr merge $prNumber --squash --admin --subject "docs(bench): decoupled-generator capacity recipe + framing (#$prNumber)"
```

The `docs:` prefix is the established convention for this repo and (per the auto-memory note saved earlier this week) maps to **no version bump** under release-please's default config. That's correct for a docs-only PR.

**Step 5: Confirm no release-please PR opened**

```powershell
Start-Sleep -Seconds 30
gh pr list --label autorelease:pending --limit 3
```

Expected output: **empty** (or the existing release-please PRs unchanged — no new `chore(main): release X.Y.Z` PR triggered by this merge).

If release-please DOES open a new PR, that's a release-please-config issue worth investigating, but not blocking — the docs landed.

**Step 6: Final report**

```powershell
git fetch origin main
git log origin/main --oneline -3
gh pr view $prNumber --json mergeCommit -q .mergeCommit.oid
```

Report:
- PR number
- All-green CI check summary
- Squash-merge SHA on `main`
- No release-please PR opened
- Final state of `origin/main` (top-3 commits)

---

## Notes

- **Each task ships independently.** Tasks 1, 2, and 4 each have their own commit. Task 3 is a verification gate with no commit. Task 5 is the push/PR/merge.
- **No release-please bump.** `docs:` prefix → no version change. The auto-memory note `project_release_please_perf_maps_to_patch.md` documents the conventional-commits → semver mapping in this repo.
- **No tests to write.** This is documentation. The "test" is Task 3 — running the recipe and confirming it works.
- **No backwards-compat concerns.** Docs additions don't affect adopters. The existing CI workflow continues to run exactly as before.
