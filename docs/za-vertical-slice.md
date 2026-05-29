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

The bench has three methods, each removing one layer:

- **`PlaceOrder_FullPipeline`** — HTTP → JWT → endpoint policy → mediator `[RequirePolicy]` → `[Validate]` → handler → EF. The full production code path.
- **`PlaceOrder_MediatorDirect`** — mediator pipeline (authz + validation) → handler → EF. HTTP + JWT bypassed.
- **`PlaceOrder_HandlerDirect`** — raw handler invocation against the scoped `DbContext`. Mediator, validation, authorization all bypassed.

`[Params(DbBackend.Sqlite, DbBackend.Postgres)]` cross-products each method against both backends so the deltas attribute cleanly.

**Setup notes:**

- **SQLite** profile uses an in-memory connection (`DataSource=:memory:`); schema applied via the production `Program.cs` `MigrateAsync()` path.
- **Postgres** profile creates a fresh per-process database (`bench_<guid8>`) and applies the EF runtime model via `EnsureCreated()`. The bench sets `Database:SchemaStrategy=Skip` so `Program.cs`'s startup migration is bypassed (existing Sqlite-typed migrations don't translate to Postgres DDL).

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

### Reading the deltas

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

### What this tells us

**The ZA framework cost is provider-independent.** The HTTP/JWT/JSON layer costs ~300 μs regardless of backend; the mediator + validation + authorization pipeline costs 65–93 μs regardless of backend. The backend delta lives almost entirely in the EF baseline (`HandlerDirect`): 372 μs on in-memory SQLite, 831 μs on localhost Postgres. That ~460 μs shift is the per-statement Postgres latency + WAL flush + network stack — *not* framework cost.

**Allocations confirm this.** Per-method allocations are within 1 KB across backends (`117.86 KB ↔ 117.14 KB`, `90.83 KB ↔ 89.62 KB`, `86.67 KB ↔ 85.34 KB`). Same framework, same allocation profile, different storage backend.

**The SQLite numbers under-state real-world latency** — in-memory SQLite has zero I/O and a single-process lock. The Postgres numbers, by contrast, exercise a real connection pool and WAL flush. For capacity planning, anchor on the Postgres row; for regression-detection of framework changes, the Sqlite row is the tighter signal.

**Pipeline cost as a fraction of total request time:**

- On SQLite (in-memory): ZA pipeline (mediator + validation + authz) = 65 μs / 738.7 μs ≈ **9%** of full-pipeline time.
- On Postgres (localhost): ZA pipeline = 93 μs / 1,224.1 μs ≈ **8%** of full-pipeline time.

Either way, EF + ASP.NET dominate the budget. The ZA framework hot path is well under 100 μs end-to-end through the mediator + validation + authorization layers combined.

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
Database__SchemaStrategy=EnsureCreated \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
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

### Numbers — `Benchmarks (manual)` workflow run [26622051695](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26622051695)

500 concurrent VUs, 30s, scenario `read_order_by_id` (`GET /orders/{id}` against a seeded set of 1000 orders):

| Metric | Value |
|---|---:|
| Total requests | 76,726 |
| OK | 76,275 |
| Fail | 451 (0.59%, all `operation timeout`) |
| **RPS** | **2,542** |
| Latency p50 / p95 / p99 | 165 ms / 308 ms / 387 ms |
| Latency mean / max | ~200 ms / ~2 s |

The SUT, NBomber, and Postgres all run on the same GHA Linux runner (AMD EPYC 7763, 4 logical cores, .NET SDK 10.0.300). The connection-string `Maximum Pool Size=500` matches the NBomber VU count and Postgres `max_connections=500`; aligning the three knobs prevents server-side rejection during steady-state.

### Reading the numbers

**vs. file-SQLite baseline.** The same scenario against file-backed SQLite was historically capped at ~470 RPS by SQLite's single-process file lock. Switching the SUT to Postgres (same scenario, same hardware) lifts the ceiling to **2,542 RPS — about 5×**. Postgres handles concurrent reads via MVCC; file-SQLite serializes them.

**Latency-vs-throughput.** Looking at this section and the BDN `WritePipelineBench` numbers earlier on this page together: BDN-Postgres is *slower per request* than in-memory SQLite (1,217 μs vs 738 μs for the full pipeline) but NBomber-Postgres is *faster overall* than file-SQLite (2,542 RPS vs ~470 RPS). Both findings are consistent — in-memory SQLite "wins" single-threaded latency by skipping real I/O; Postgres "wins" throughput because it handles concurrency properly. The ZA framework cost itself is **provider-independent** in both directions (per-method allocations across backends differ by ≤1 KB, pipeline-layer deltas of ~300 μs and 65–93 μs hold across both providers).

**The per-request budget.** SUT-log inspection of EF query timings under load shows a bi-modal distribution: ~half the queries return in **~1 ms** (uncontended-path SELECT against the primary key), the other half cluster at **~67–74 ms** (connection-pool acquire + MVCC transaction-snapshot setup under 500-VU pressure). The 67–74 ms × 500 concurrent VUs gives the ~190 ms mean request latency that Little's law predicts. Framework-layer optimizations (JWT validation cache, compiled queries, mediator dispatch tuning) are sub-millisecond against this budget — they would not move the RPS ceiling materially. Reaching higher throughput from this number requires architectural changes (HTTP-layer caching, larger Postgres host, or a different scenario shape), not framework tuning.
