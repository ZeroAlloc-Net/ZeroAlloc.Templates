# #189 — dotnet-counters capture on za-vs under sustained NBomber load

**Date:** 2026-06-08
**Host:** i9-12900HK laptop, Docker Desktop, Windows 11
**SUT:** `content/za-vertical-slice` on `main` (post-#193, JIT — no NativeAOT publish; see methodology)
**Tool:** `dotnet-counters` 9.0.661903, `--counters System.Runtime,Microsoft.AspNetCore.Hosting,Npgsql --format csv --refresh-interval 1`

## Goal

Path B of the #189 investigation: identify which counter saturates first as the za-vs SUT moves from healthy to collapse under NBomber load. Candidates: ThreadPool starvation, Npgsql pool contention, GC pause coupling, allocation-rate saturation.

## Methodology

Recipe followed largely from `docs/benchmarks/capacity-recipe.md`:

- Postgres 17 pinned to cores 0-1 (`--cpuset-cpus="0-1"`, `max_connections=2000`).
- SUT launched via `dotnet run -c Release --no-build` (Windows + JIT, ProcessorAffinity not propagated to child — the capacity-recipe caveat).
- LoadTest project built in Release; NBomber on host, no CPU pinning.
- `dotnet-counters collect` attached to MyApp.exe child PID before each NBomber run.
- Sustain window: NBomber `ramping_inject` 10s → `inject` 30s. Analysis skips the first 12s of capture (10s ramp + 2s safety) to focus on sustain.

### Methodology caveats (honest)

Both runs **collapsed deeply** — neither the 5k nor the 8k target matched the `2026-06-05-nbomber-ceiling-sweep.md` reference (where 5k delivered 4312 actual RPS with 0 fails). This run got:

| Target | Actual RPS (ok) | Fail count | p50 ok (ms) | Reference (06-05) actual RPS |
|---|---:|---:|---:|---:|
| 5k | 1198 | 124,581 | 7,803 | 4,312 |
| 8k | 2481 | 176,741 | 8,176 | 5,395 |

Likely contributors to the discrepancy:

1. **No NativeAOT publish.** The repo wants NativeAOT (`PublishAot=true`); the publish on this host failed (`vswhere.exe` not found in PATH for the AOT toolchain link step), so the SUT runs JIT. JIT cold-start + tiered compilation explain part of the gap on a 4-core slice.
2. **dotnet-counters overhead.** Attaching an EventPipe listener adds non-zero cost; with the SUT already on the edge, this likely tips it over.
3. **Background machine noise.** A non-quiet machine.

Because both runs were collapsed, this is **not** the clean 5k-healthy / 8k-collapsing comparison the plan envisioned. It is instead **mid-collapse @ 5k offered vs deeper-collapse @ 8k offered** — still useful for ranking which counters dominate in saturation, less useful for the "first to cross threshold" narrative.

## Counter snapshot — side by side (sustain window, mean / peak)

Counter dimensions trimmed for readability (full data in `counters-vs-5k.csv` / `counters-vs-8k.csv` in the repo root):

| Counter | Mean @ 5k | Peak @ 5k | Mean @ 8k | Peak @ 8k |
|---|---:|---:|---:|---:|
| **db.client.connection.npgsql.pending_requests** | **2,149** | **4,077** | **2,070** | **3,596** |
| db.client.connection.count [used] | 247 | **500** | 311 | **500** |
| db.client.connection.count [idle] | 66 | 215 | 148 | 500 |
| db.client.connection.max | 500 | 500 | 500 | 500 |
| db.client.operation.npgsql.executing | 80 | 393 | **285** | **500** |
| db.client.operation.duration (s, p99) | 0.67 | 5.24 | 1.02 | 4.36 |
| http.server.active_requests [GET] | 2,226 | 4,096 | 2,333 | 4,095 |
| http.server.request.duration [200, p50] (s) | 2.91 | 9.44 | 1.25 | 2.18 |
| http.server.request.duration [200, p99] (s) | 6.06 | 17.50 | 2.68 | 6.93 |
| dotnet.thread_pool.queue.length (delta/s) | mean ≈0 | 767 | mean ≈0 | **2,271** |
| dotnet.thread_pool.thread.count (delta/s) | mean ≈0 | 20 | mean ≈0 | 17 |
| dotnet.thread_pool.work_item.count (cumulative/s) | 3,254 | 12,938 | 7,264 | 14,876 |
| dotnet.gc.pause.time (s / 1s)  | 0.02 | 0.22 | 0.03 | 0.15 |
| dotnet.gc.heap.total_allocated (MB/s) | 19.8 | 225 | 31.6 | 74.6 |
| dotnet.gc.collections gen0 (/s) | 0.24 | 2 | 0.21 | 2 |
| dotnet.gc.collections gen2 (/s) | 0.05 | 1 | 0.06 | 1 |
| dotnet.monitor.lock_contentions (/s) | 10.2 | 85 | 51.2 | 283 |
| dotnet.process.cpu.time user (s/s) | 0.64 | 2.33 | 0.88 | 2.16 |
| dotnet.process.memory.working_set (MB) | 350 | 509 | 309 | 401 |
| dotnet.exceptions OperationCanceled (/s) | 157 | 3,724 | 346 | 3,823 |

## Saturation finding

**The dominant saturation signature is the Npgsql connection pool.**

- `db.client.connection.count [state=used]` **pegs at the pool ceiling of 500** at peak in BOTH runs. The pool is fully consumed.
- `db.client.connection.npgsql.pending_requests` — the queue of requests **waiting for a connection** — sits at **mean 2,069–2,149 with peaks of 3,596–4,077**. In a healthy system this counter should be near 0.
- `http.server.active_requests` sits at the SocketsHttpHandler / Kestrel inbound-connection cap (~4,096). Every incoming request that needs DB enters the Npgsql wait queue, and that queue dominates per-request latency.
- `db.client.operation.duration` p99 (the time to actually run a SQL command once a connection is acquired) stays at **0.67–1.02s** — Postgres itself is not the bottleneck; **acquiring a connection is**.

Other candidates are clearly **not** the dominant signal:

- **ThreadPool starvation:** `thread.count` (delta/s) averages ~0 with peak deltas of 17–20. The runtime is not desperately growing worker threads. `queue.length` (delta/s) does spike to 2,271 at 8k, but it's a 1-second peak, not sustained — the work items are predominantly continuations from awaited DB calls, not CPU-bound work the pool can't drain.
- **GC pause coupling:** `gc.pause.time` averages 2–3 % of wall-clock. Threshold of concern is ≥10 %. Gen2 collections happen at ~0.05/s. Not the bottleneck.
- **Allocation-rate saturation:** 20–32 MB/s mean, 75–225 MB/s peak. Modern GC drains many hundreds of MB/s without coupling latency.
- **Lock contention:** 10/s → 51/s is a 5× increase but absolute numbers are tiny; lock contentions in the high-hundreds/s would be the threshold of concern.

## Verdict

**Npgsql connection pool exhaustion is the strongest candidate for the load-coupled bottleneck driving za-vs's per-request cost gap vs za-clean.** The pool is sized at 500 (`Maximum Pool Size=500` in the LoadTest connection string), and under sustained load `used=500` with ~2,000 requests queued waiting.

**Why this is consistent with the 8.6× per-request cost gap:** the vs read pipeline (per `2026-06-07-189-read-pipeline-vs.md`) opens a connection per request (no per-handler scope reuse / no command batching). At ~4,000 in-flight requests against a 500-connection pool, every request waits on a queue of ~8 ahead of it before getting its turn. That queue *is* the per-request cost premium — it scales with offered load and explains why the gap widens sharply between 5k and 8k targets in healthy runs.

**Why this isn't proof on its own:** the absolute capacity numbers in this run are off-reference (collapsed at 5k, where reference says it should be healthy). The pool-exhaustion signature would also appear in *any* sufficiently overloaded JIT-cold SUT — it's the natural symptom of "too many requests, finite pool". A clean test with NativeAOT, no counter overhead, and 5k healthy would be needed to confirm that connection-pool wait is *also* the bottleneck in the healthy-vs-collapsing transition (rather than only in the collapsed state).

**What to try next (if this turns out to be confirmed):**

1. Test a larger pool (`Maximum Pool Size=2000`) — if the wait queue shrinks but throughput doesn't grow, Postgres or threadpool is the real next ceiling.
2. Reduce **connections per request** at the vs read pipeline (issue acquired connection earlier, release immediately after read, batch where possible).
3. Compare to za-clean under the same recipe — clean's `pending_requests` should be near-zero if its read pipeline is genuinely cheaper at the connection-handling level.

## Files

All artifacts live alongside this doc in `docs/benchmarks/`:

- `2026-06-08-189-counters-vs-5k.csv` (565 KB, 91 timestamps, 63 distinct counter rows per snapshot)
- `2026-06-08-189-counters-vs-8k.csv` (465 KB, 75 timestamps)
- `2026-06-08-189-analyze-counters.ps1` — reproducible analysis script (pwsh)
- `2026-06-08-189-counter-comparison.json` — machine-readable side-by-side
- `2026-06-08-189-nbomber-5k.log`, `2026-06-08-189-nbomber-8k.log` — NBomber stdout for each leg
