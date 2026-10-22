# Template capacity, measured 2026-10-22

How many requests per second `za-clean` and `za-vertical-slice` serve, what limited them, and what
changed. The previous capacity figures came from June: they predate output caching, ran the JIT build
because the NativeAOT publish failed, and were capped by the NBomber injector rather than by the app
(see [the ceiling sweep](2026-06-05-nbomber-ceiling-sweep.md)).

> Throughput and latency are shown as same-run ratios against the build before this change.
> Absolute requests per second are not published; they depend on the machine.

## Setup

```
12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26300), .NET SDK 10.0.402
oha 1.16.0, Postgres 17 in Docker Desktop (WSL2 VM, 4 vCPUs), no other containers running
```

- The app is the NativeAOT `win-x64` publish of each template, pinned to logical CPUs 4–7 (two
  performance cores). `oha` is pinned to the eight efficiency cores, so the load generator never
  competes with the app.
- Every scenario starts a fresh database and a fresh app process, seeds 1,000 orders, warms up for 5 s
  and measures for 15 s with 256 connections. JWT authentication is on, as in production.
- The baseline and the new build were alternated, three rounds each; the tables give the median.
- [`scripts/capacity.ps1`](../../scripts/capacity.ps1) is the harness.

| Scenario | What it sends |
|---|---|
| Cache hit | `GET /orders/1`, always served from the output cache |
| Uncached read | `GET /orders/{random id}` with `OrderByIdTtlSeconds=0`: mediator, ZA.ORM read, JSON |
| Mixed | `GET /orders/{random id}` with the cache on, while 100 orders per second are created |
| Write | `POST /orders` |

## What limited throughput

**On an uncached read the app was the bottleneck, not Postgres.** During the uncached-read scenario the
app used 3.95 of its 4 logical CPUs while Postgres used 2.2 of its 4. Before this change an uncached
read cost about 4.6 cache hits of app CPU.

**Writes emptied the read cache.** `POST /orders` evicted every cached `GET /orders/{id}`. At only 100
writes per second the mixed scenario served under half of what the cache-hit scenario did.

## Experiments

Each setting was tried on its own on `za-clean`, alternated with the baseline over three rounds. Only
the rows in bold were kept.

| Change | Cache hit | Uncached read | Write |
|---|---:|---:|---:|
| **Npgsql `Max Auto Prepare=20`** | 0.99 | **1.15** | **1.22** |
| **Npgsql `Multiplexing=true`**, with auto-prepare | 0.98 | **1.83** | **1.20** |
| `Maximum Pool Size=32` instead of 500 | 0.95 | 0.95 | 0.86 |
| `No Reset On Close=true` | 0.98 | 1.16, ranges overlap | 1.10, ranges overlap |
| `Telemetry:TraceSampleRatio=0` | 1.01 | 0.99 | 0.99 |
| `DOTNET_GCDynamicAdaptationMode=0`, DATAS off | 0.99 | 0.92 | 0.98 |
| JIT instead of NativeAOT | 1.03 | 0.82 | 0.96 |

- **Auto-prepare.** Npgsql prepares a statement after it has run a few times on a physical connection,
  so Postgres skips parse and plan on every later call. It is off by default, and every statement in
  these templates repeats.
- **Multiplexing.** Concurrent requests share physical connections, and their commands are written to
  the socket in batches. The slowest multiplexing run beat the fastest baseline run.
- **Pool size** made no throughput difference. A smaller pool halved uncached-read p99, but cost 14% on
  writes.
- **Trace sampling and GC mode** made no measurable difference. The OTLP exporter batches off the
  request thread, so a span per request costs little here.
- **NativeAOT costs no throughput here.** The JIT build was slower on uncached reads, and its 1.03 on
  cache hits is inside the run-to-run spread.

## Result

Medians of three alternated rounds, new build against the build before this change:

| Scenario | `za-clean` | `za-vertical-slice` |
|---|---:|---:|
| Cache hit | 0.99 | 1.01 |
| Uncached read | **2.29** | **1.84** |
| Mixed: reads | **2.03** | **2.32** |
| Mixed: read p99 | **0.48** | **0.39** |
| Write | **1.43** | 1.03 |
| Write p99 | 0.70 | **2.7, worse** |

After the change an uncached read costs about two cache hits of app CPU instead of 4.6, and with 100
writes per second the mixed scenario serves about 90% of the cache-hit rate.

### The trade-off: single-statement writes under multiplexing

`za-vertical-slice` writes are one autocommit `INSERT` each. Under multiplexing, Postgres commits the
statements pipelined onto one connection one after another, so one slow commit delays the ones queued
behind it. At saturation the write p99 rose 2.7× while throughput and p50 held:

| `za-vertical-slice` write | Throughput | p50 | p99 |
|---|---:|---:|---:|
| Baseline | 1.00 | 1.00 | 1.00 |
| Auto-prepare only | 1.05 | 0.95 | 0.98 |
| Auto-prepare and multiplexing | 1.00 | 0.79 | 2.74 |

`za-clean` does not pay this: its write runs in a transaction, which holds a connection to itself, and
its write p99 fell. A write-heavy service whose tail latency matters more than read throughput sets
`Multiplexing=false` in its connection string and keeps auto-prepare.

## Allocations

Per-request allocations did not change. BenchmarkDotNet, three launches each, bytes per request:

| Path | `za-clean` before → after | `za-vertical-slice` before → after |
|---|---:|---:|
| `GET /orders/{id}`, cache hit | 24,723 → 24,723 | 24,636 → 24,635 |
| `GET /orders/{id}`, uncached | 28,738 → 28,750 | 26,343 → 26,325 |
| `POST /orders`, Postgres | 38,551 → 38,467 | 34,537 → 34,442 |

The GET endpoints add the per-id cache tag only to a response the output cache will store, so a
response that is not stored allocates nothing for it.

## Changes

- **Per-id cache eviction.** Creating an order or customer evicts nothing, because only 200 responses
  are cached and a new id was never served. The GET endpoints tag each cached response with its id,
  and `CancelOrder` evicts that one order.
- **`OrderByIdTtlSeconds=0` turns the cache off.** It used to pass a zero expiry to the memory store,
  which threw on every response it tried to store; the middleware logged the exception and served the
  response uncached.
- **Npgsql defaults.** Both templates set `Max Auto Prepare=20` and `Multiplexing=true` on a Postgres
  connection string that does not set them. Multiplexing needs the async Npgsql API throughout, which
  ZA.ORM uses, and keeps no session state between commands: no `SET`, temporary tables or `LISTEN`
  outside a transaction. Every endpoint of both templates was exercised against Postgres with
  multiplexing on.

## Reproducing

Publish the template with NativeAOT, start Postgres, and run the harness from PowerShell. On Windows
the NativeAOT link step calls `vswhere.exe` from `PATH`; add
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH` if the publish fails with
`'vswhere.exe' is not recognized`.

```powershell
dotnet publish content/za-clean/src/MyApp.Api -c Release -r win-x64 -o .publish/clean
docker run -d --name pgbench -p 55432:5432 -e POSTGRES_PASSWORD=postgres postgres:17 -c max_connections=2000
./scripts/capacity.ps1 -Exe .publish/clean/MyApp.Api.exe -Label clean -Oha path/to/oha.exe
```

Pick `-SutMask` and `-LoadMask` for your CPU so the app and `oha` never share a core. Run the build
you compare against in alternation with yours, and compare medians: single runs on this machine varied
by up to 20%.
