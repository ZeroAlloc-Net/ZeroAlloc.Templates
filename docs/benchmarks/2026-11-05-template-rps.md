# Requests per second, measured 2026-11-05

How many requests per second the NativeAOT builds of `za-clean` and `za-vertical-slice` serve on one
machine. This complements [the 2026-10-22 capacity report](2026-10-22-template-capacity.md), which
publishes same-run ratios only. These are absolute figures for **this machine**; they do not transfer to
other hardware. Allocations, not requests per second, remain the portable measure.

## Setup

```
12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26300), .NET SDK 10.0.402
oha 1.16.0, Postgres 17 in Docker Desktop (WSL2), no other containers running
```

- NativeAOT `win-x64` publish of each template from `main` at `5bd7958`. The app is pinned to logical
  CPUs 4-7 (two performance cores), `oha` to the eight efficiency cores.
- Each scenario starts a fresh database and a fresh app process, seeds 1,000 orders, warms up for 5 s
  and measures for 15 s with 256 connections (64 for writes). JWT authentication is on.
- Three rounds, templates alternated within each round. The tables give the median and the range.
- Every response was a success: 200 for reads, 201 for writes. [`scripts/capacity.ps1`](../../scripts/capacity.ps1)
  is the harness.

## Result

Requests per second, median of three rounds (range in brackets):

| Scenario | `za-clean` | `za-vertical-slice` |
|---|---:|---:|
| Cache hit, `GET /orders/1` from the output cache | 39,011 (37,871-39,113) | 38,425 (32,167-39,625) |
| Uncached read, `GET /orders/{random id}`: mediator, ZA.ORM, JSON | 15,960 (12,956-16,395) | 16,867 (15,790-17,378) |
| Mixed, cached reads while 100 orders per second are created | 32,192 (31,843-32,988) | 34,285 (32,599-35,352) |
| Write, `POST /orders` | 3,267 (3,210-3,709) | 6,768 (6,714-8,016) |

Median latency and 99th percentile, same runs:

| Scenario | `za-clean` p50 / p99 | `za-vertical-slice` p50 / p99 |
|---|---:|---:|
| Cache hit | 4.0 / 48.7 ms | 4.3 / 54.9 ms |
| Uncached read | 9.3 / 125.2 ms | 8.3 / 124.8 ms |
| Write | 17.1 / 48.9 ms | 7.0 / 50.9 ms |

## Allocations

Bytes allocated per request through the full middleware stack, from `ReadPipelineBench` and
`WritePipelineBench` in each template, BenchmarkDotNet with `--launchCount 3`, run out of process.
Unlike requests per second these do not depend on the machine. KB is 1,024 bytes.

| Path | `za-clean` | `za-vertical-slice` |
|---|---:|---:|
| `GET /orders/{id}`, cache hit (SQLite) | 24.2 KB | 24.1 KB |
| `GET /orders/{id}`, uncached (SQLite) | 27.7 KB | 25.6 KB |
| `POST /orders` (Postgres) | 37.9 KB | 33.9 KB |

- These agree with the [2026-10-22 report](2026-10-22-template-capacity.md) to within about 1%, so the
  policy change in #574 and the OpenAPI option in #590 did not move the hot path.
- The read benchmark runs on SQLite only; the write benchmark was run against Postgres 17. The mixed
  scenario has no allocation benchmark, so the README shows n/a for it.
- `--inProcess` fails on these benchmarks: `WebApplicationFactory` cannot find its content root in the
  in-process toolchain, so every row reports `NA` while BenchmarkDotNet still exits 0.

## Reading the numbers

- **The two templates read at the same speed.** The ranges overlap on every read scenario, so these
  runs do not support calling either one faster. Architecture style costs nothing at run time here.
- **Writes differ.** `za-vertical-slice` writes one autocommit `INSERT`; `za-clean` writes inside a
  transaction. That is a difference in the endpoint, not in the folder layout.
- **Spread is real.** Single runs varied by up to 25% (uncached read on `za-clean`). The median is more
  trustworthy than the best run.
- **This is one machine.** The app had four logical CPUs and the database ran in a VM on the same host.
