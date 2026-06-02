---
id: za-vertical-slice
title: ZeroAlloc Vertical Slice Template
description: Tour of `dotnet new za-vertical-slice` — feature-per-folder layout, source-gen mediator + validation + authorization, and the WritePipelineBench framework-cost story.
sidebar_position: 2
---

# ZeroAlloc Vertical Slice Template

`za-vertical-slice` is a `dotnet new` template that scaffolds a single-project Vertical Slice Architecture Web API on the ZeroAlloc.\* ecosystem. Where `za-clean` splits the codebase by *technical layer* (Domain / Application / Infrastructure / Api, four csprojs, dependency direction strictly inward), this template splits by *use case*: one folder per feature, one file per slice, every slice owns its request + validator + handler + endpoint + entity. The 10-package ZeroAlloc showcase is identical to `za-clean`'s; the wiring is inverted.

This page is a tour of the **benchmark harness** and what its numbers mean — particularly the `WritePipelineBench`'s 3 × 2 attribution matrix that B2 added (PR #140). For the full template tour, start with the per-template `README.md` after `dotnet new za-vertical-slice -o MyApp`.

## Quickstart

```bash
dotnet new install ZeroAlloc.Templates
dotnet new za-vertical-slice -o MyApp
cd MyApp && dotnet run --project src/MyApp
```

The Api boots, applies its EF Core SQLite migrations on startup, and listens on the kestrel default. In another shell:

```bash
$ curl http://localhost:5000/healthz
{"status":"ok"}
```

## Benchmark layout

Two BDN projects + one NBomber load test live under `benchmarks/`:

- **`MyApp.Benchmarks.Primitives`** — `PrimitivesBench` exercises each ZA layer standalone: mediator dispatch, validator, value-object construction, mapping. No ASP.NET, no EF, no HTTP. Numbers in ns/op, allocations in bytes. These deliver on the "zero-allocation through the framework hot path" claim.
- **`MyApp.Benchmarks`** — `WritePipelineBench` hosts the API via `WebApplicationFactory<Program>` and runs `POST /orders` through three attribution layers (full HTTP path / mediator-direct / handler-direct) × two database backends (SQLite in-memory / Postgres localhost). Reports μs and per-request allocation. The 3 × 2 cross-product reveals where time is spent.
- **`MyApp.LoadTest`** — NBomber scenario at 500 VUs against real Kestrel; see "Load testing against Postgres" below. Run by the manual `nbomber-postgres-vs` CI job.

## WritePipelineBench — the framework-cost story

Post-swap (PR #152: EF Core → ZA.ORM 1.1), the bench has collapsed to a single `WritePipeline` method per backend. The previous 3-method attribution matrix (`FullPipeline` / `MediatorDirect` / `HandlerDirect`) was useful when EF Core's per-request cost dominated and you wanted to know "which layer is expensive?" — it's preserved in the historical section below for diff-over-time context. The post-swap bench attributes only the full HTTP path because the dominant cost is now the wire to Postgres; per-slice attribution wasn't interesting anymore once the persistence layer became light enough to disappear into the noise.

**Setup notes:**

- **SQLite** profile uses an in-memory connection (`DataSource=:memory:`); schema applied via the production `Program.cs` `ApplyEmbeddedSchemaAsync` path reading `schema.sql`.
- **Postgres** profile creates a fresh per-process database (`bench_<guid8>`) and applies `schema.postgres.sql` via the same production path. AOT-friendly — no reflection. The bench sets `Database:Provider=Postgres`; Program.cs picks the right embedded resource.

### Numbers — `Benchmarks (manual)` workflow run [26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747) (post-swap)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300, .NET 10.0.8, X64 RyuJIT x86-64-v3
```

| Method        | Backend  |     Mean | Error    | StdDev   | Gen0   | Allocated |
|-------------- |--------- |---------:|---------:|---------:|-------:|----------:|
| WritePipeline | Sqlite   | 186.2 μs |  2.89 μs |  4.50 μs | 1.9531 |  29.15 KB |
| WritePipeline | Postgres | 658.7 μs | 12.81 μs | 19.94 μs | 1.9531 |  31.97 KB |

Primitives (slice-level, single-method; AMD EPYC 9V74 same run):

| Method                  | Mean       | Allocated |
|------------------------ |-----------:|----------:|
| `TypedId_Construct`     |  0.0026 ns |       0 B |
| `Money_TryCreate_Success` |  3.439 ns |       0 B |
| `Money_TryCreate_Failure` | 10.329 ns |       0 B |
| `Result_Success`        |   7.323 ns |       0 B |
| `Result_Failure`        |   0.398 ns |       0 B |
| `Validator_Generated`   |   4.175 ns |       0 B |

### Reading the numbers

**EF Core → ZA.ORM 1.1 delta.** Comparing the post-swap `WritePipeline` row against the pre-swap `PlaceOrder_FullPipeline` row in the historical section below: Sqlite 738.7 μs → 186.2 μs (−75%) / 117.9 KB → 29.2 KB (−75%). Postgres 1,224.1 μs → 658.7 μs (−46%) / 117.1 KB → 32.0 KB (−73%). The Sqlite time-win is much larger because Postgres latency is dominated by the network round-trip — the framework-overhead share left over after the wire is still ~73% leaner. The allocation profile reflects ZA.ORM having no change tracker, no model snapshot, no proxy materialisation: `PlaceOrderHandler` calls a `[Command]`-generated `InsertOrderAsync` that executes a direct `INSERT` against `IAsyncDbConnection`.

**The ZA framework cost is provider-independent.** Allocations differ by ~3 KB across backends (29.15 KB ↔ 31.97 KB) — the gap is Npgsql command/parameter cost vs Microsoft.Data.Sqlite's lighter wire, not the framework hot path. The TypedId / Money / Result / Validator primitives all return in single-digit ns with 0 B; `TypedId_Construct` inlines to nothing (0.0026 ns is the measurement floor).

**The SQLite numbers under-state real-world latency** — in-memory SQLite has zero I/O and a single-process lock. The Postgres numbers, by contrast, exercise a real connection pool and WAL flush. For capacity planning, anchor on the Postgres row; for regression-detection of framework changes, the Sqlite row is the tighter signal.

## WritePipelineBench — pre-swap baseline (historical)

The pre-swap bench attributed cost across three slice depths via separate benchmark methods (`PlaceOrder_FullPipeline` / `PlaceOrder_MediatorDirect` / `PlaceOrder_HandlerDirect`); the post-swap bench above collapses to a single `WritePipeline` row per backend — the whole point of the swap was to make the persistence layer light enough that the per-slice attribution wasn't interesting anymore.

### Numbers — `Benchmarks (manual)` workflow run [26592448470](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26592448470)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300, .NET 10.0.8, X64 RyuJIT x86-64-v3
```

| Method                    | Backend  |       Mean | Allocated |
|-------------------------- |--------- |-----------:|----------:|
| PlaceOrder_FullPipeline   | Sqlite   |   738.7 μs | 117.86 KB |
| PlaceOrder_MediatorDirect | Sqlite   |   437.2 μs |  90.83 KB |
| PlaceOrder_HandlerDirect  | Sqlite   |   372.3 μs |  86.67 KB |
| PlaceOrder_FullPipeline   | Postgres | 1,224.1 μs | 117.14 KB |
| PlaceOrder_MediatorDirect | Postgres |   924.6 μs |  89.62 KB |
| PlaceOrder_HandlerDirect  | Postgres |   831.3 μs |  85.34 KB |

### Reading the deltas (historical)

**Layer deltas — within each backend:**

|                              | Sqlite | Postgres |
|------------------------------|-------:|---------:|
| HTTP + JWT + JSON layer (Full − MediatorDirect) | 302 μs | 300 μs |
| Mediator + validation + authorization pipeline (MediatorDirect − HandlerDirect) | 65 μs | 93 μs |
| EF baseline (HandlerDirect)  | 372 μs | 831 μs |

**Backend deltas — within each method:**

| Method                    | Sqlite | Postgres | Delta |
|-------------------------- |-------:|---------:|------:|
| PlaceOrder_FullPipeline   | 738.7 μs | 1,224.1 μs | +485 μs |
| PlaceOrder_MediatorDirect | 437.2 μs |   924.6 μs | +487 μs |
| PlaceOrder_HandlerDirect  | 372.3 μs |   831.3 μs | +459 μs |

**Pipeline cost as a fraction of total request time (pre-swap):**

- On SQLite (in-memory): ZA pipeline (mediator + validation + authz) = 65 μs / 738.7 μs ≈ **9%** of full-pipeline time.
- On Postgres (localhost): ZA pipeline = 93 μs / 1,224.1 μs ≈ **8%** of full-pipeline time.

Either way, EF + ASP.NET dominated the budget. Post-swap, the framework hot path is unchanged at the primitive level (validator 4 ns, mediator dispatch in the low ns); the persistence layer dropped by ~3–4× and is no longer the dominant per-request cost.

## Reproducing locally

The SQLite profile rows run anywhere:

```bash
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"
```

The Postgres profile rows need a localhost Postgres on `5432`. Start one:

```bash
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bench \
  --name bench-pg postgres:17

dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

docker stop bench-pg
```

CI provisions Postgres via the `services:` block in `.github/workflows/benchmarks.yml`; the workflow is manual-only (`workflow_dispatch`), so trigger it from the Actions UI on the branch you want numbers for.

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
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
dotnet run -c Release --project src/MyApp &

# 3. Wait for /healthz, then run NBomber
until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
dotnet run -c Release --project benchmarks/MyApp.LoadTest

# Cleanup
kill %1; docker stop myapp-load-pg
```

Startup applies the embedded `schema.postgres.sql` via `ApplyEmbeddedSchemaAsync` — no EF reflection at runtime. The script is idempotent (checks `__EFMigrationsHistory` before applying), so re-runs against an existing load-test database are safe. After entity changes, regenerate both providers' migrations + schema scripts via `tools/regen-schema.sh` (or `tools/regen-schema.ps1`).

### CI

The `nbomber-postgres-vs` job in `.github/workflows/benchmarks.yml` runs the recipe above end-to-end on every manual workflow trigger. Artifacts:

- `nbomber-za-vertical-slice-postgres` — NBomber's HTML / CSV / Markdown reports.
- `nbomber-sut-log` — the SUT's stdout/stderr (kept short, 7-day retention).

### Numbers — `Benchmarks (manual)` workflow run [26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747) (post-swap)

NBomber open-model `Inject(rate=5000/s)`, 30 s steady-state + 10 s ramp, scenario `read_order_by_id` (`GET /orders/{id}` against a seeded set of 1000 orders):

| Metric | Value |
|---|---:|
| OK / fail | **172,500 / 0** |
| **RPS (ok)** | **4,312.5** |
| Latency min / mean / max | 0.43 / 125.27 / 1,702.02 ms |
| Latency StdDev | 267.83 ms |
| Latency p50 / p75 / p95 / p99 | 36.99 / 87.62 / 860.67 / 1,318.91 ms |

The SUT, NBomber, and Postgres all run on the same GHA Linux runner (AMD EPYC 7763, 4 logical cores, .NET SDK 10.0.300). The connection-string `Maximum Pool Size=500` matches Postgres `max_connections=500`.

### Reading the numbers (post-swap)

**vs. the pre-swap EF-era Postgres run (`run 26622051695`, kept below).** Same scenario, same hardware, same SUT shape — only the persistence layer swapped (EF Core 10 → ZA.ORM 1.1) and the load shape moved from closed-model 500-VU to open-model 5k-RPS inject. Headline: 2,542 RPS → 4,312 RPS (+70%), p50 165 ms → 37 ms (−78%), zero failures vs 0.59% timeouts. The tail widens (p99 387 ms → 1,319 ms) because the load shape changed to open-model `Inject` (no closed-loop backpressure) — under steady inject pressure, individual request latencies are bounded by Postgres-wire + Kestrel queueing, and the tail reflects the queue-depth swings that closed-loop concurrency previously smoothed out.

**vs. file-SQLite baseline (EF-era closed-model).** The same scenario against file-backed SQLite was historically capped at ~470 RPS by SQLite's single-process file lock. Post-swap Postgres lifts the ceiling to **4,312 RPS — about 9×**.

**vs. za-clean.** za-clean's equivalent post-swap NBomber-Postgres run lands at the same 4,312 RPS / 172,500 ok / 0 fail (open-model `Inject` saturates both templates at the configured rate). vs's p50 is dramatically lower (37 ms vs clean's 189 ms) reflecting the slice-direct resolution path — the slice's request → endpoint → handler walk skips Application/Infrastructure/Api layer hops. Tail shapes are comparable (vs p99 1,319 ms vs clean's 1,137 ms).

### Numbers — `Benchmarks (manual)` workflow run [26622051695](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26622051695) (pre-swap, historical)

500 concurrent VUs (closed-model), 30 s, scenario `read_order_by_id` (`GET /orders/{id}` against a seeded set of 1000 orders), EF Core 10 persistence:

| Metric | Value |
|---|---:|
| Total requests | 76,726 |
| OK | 76,275 |
| Fail | 451 (0.59%, all `operation timeout`) |
| **RPS** | **2,542** |
| Latency p50 / p95 / p99 | 165 ms / 308 ms / 387 ms |
| Latency mean / max | ~200 ms / ~2 s |

**The per-request budget (historical).** SUT-log inspection of EF query timings under load showed a bi-modal distribution: ~half the queries returned in **~1 ms** (uncontended-path SELECT against the primary key), the other half clustered at **~67–74 ms** (connection-pool acquire + MVCC transaction-snapshot setup under 500-VU pressure). The 67–74 ms × 500 concurrent VUs gave the ~190 ms mean request latency that Little's law predicted. Kept for diff-over-time context; the post-swap table above is the current baseline.
