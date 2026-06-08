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
$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500"
$env:Shipping__UseStub = "true"

# Start the SUT from the repo root so the relative --project path resolves.
# Capture stdout + stderr so any startup failure is diagnosable.
$sut = Start-Process dotnet `
  -ArgumentList @("run","-c","Release","--no-build","--project","content/za-clean/src/MyApp.Api") `
  -WorkingDirectory $PWD `
  -RedirectStandardOutput "sut.out.log" `
  -RedirectStandardError "sut.err.log" `
  -PassThru
# 0x3C = cores 2,3,4,5 (bitmask 0b111100). Affinity is applied to the dotnet
# launcher; see "Caveats" below for the limitation on Windows.
$sut.ProcessorAffinity = [IntPtr]::new(0x3C)
```

> **Why `127.0.0.1` and not `localhost`?** On Windows + Docker Desktop, Npgsql resolves `localhost` to IPv6 (`::1`) first and the Docker port-forward IPv6 path stalls the TCP handshake — connection times out after 15s. `127.0.0.1` forces IPv4 and connects in <1s.

`Shipping__UseStub=true` swaps the typed shipping client for an in-memory stub. Without it, every POST /orders during NBomber init hits a DNS failure on `shipping.example` and seeding aborts. This env var only exists on za-clean — vs has no outbound shipping call.

> **Windows pinning caveat.** `dotnet run` spawns `MyApp.Api.exe` as a child process; `ProcessorAffinity` set on the `dotnet` launcher does NOT propagate to the child on Windows. For *real* CPU pinning on Windows, publish once and launch the published binary directly:
>
> ```powershell
> dotnet publish content/za-clean/src/MyApp.Api -c Release -o ./.publish-sut
> $sut = Start-Process .\.publish-sut\MyApp.Api.exe `
>   -WorkingDirectory $PWD `
>   -RedirectStandardOutput "sut.out.log" `
>   -RedirectStandardError "sut.err.log" `
>   -PassThru
> $sut.ProcessorAffinity = [IntPtr]::new(0x3C)
> ```
>
> On Linux, `taskset -c 2-5 dotnet run` works because Linux process affinity inherits to spawned children. The `dotnet run` path on Windows leaves the SUT effectively unpinned — the recipe still works (NBomber and SUT contend less than they would without any pinning) but you lose the strong isolation property.

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

NBomber writes its report markdown to `<repo-root>/reports/<session>/nbomber_report_*.md` (its default working directory — the launcher's CWD). Find the most recent run with `Get-ChildItem -Recurse reports -Filter "nbomber_report_*.md" | Sort-Object LastWriteTime -Descending | Select-Object -First 1`. Copy interesting ones to `docs/benchmarks/<YYYY-MM-DD>-<topic>.md`.

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
ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
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
