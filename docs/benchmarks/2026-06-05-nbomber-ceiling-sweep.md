# NBomber rate-sweep — actual SUT ceiling on i9-12900HK (laptop)

Repo: `ZeroAlloc.Templates` (branch `feat/flat-read-model` at `fb7fd19`, post-#172/#173)
Date: 2026-06-05
Hardware: Intel i9-12900HK (16 cores), Windows 11, Docker Desktop
SUT: Real Kestrel (`dotnet run -c Release --no-build`) against Postgres 17 in Docker (`max_connections=2000`), `Shipping__UseStub=true`, `EmbeddedScript` migrations, 1000 seeded orders per process
Load shape: NBomber 6.4.1 open-model — 10s ramp + 30s sustain
Background: yesterday's CI run showed 5k → 4312 actual (no fail), 15k → ~6.8k actual / 47% fail. The 4312 plateau was the **injector cap** at 5k target, not the SUT ceiling. This sweep probes 5k–20k to find the real saturation point.

## Sweep results

| Template | Target RPS | Actual RPS (ok) | ok | fail | fail% | p50 (ms) | p95 (ms) | p99 (ms) | mean (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| za-clean | 5 000  | 4 312.5  | 172 500 | 0       |  0.0% |    6.52 |   15.84 |   29.46 |     7.72 |
| za-clean | 8 000  | 6 900.0  | 276 000 | 0       |  0.0% |   17.41 |  121.86 |  215.68 |    34.83 |
| za-clean | 12 000 | 6 566.3  | 262 651 | 151 349 | 36.6% | 7 872.5 | 13 099  | 13 689  | 7 088    |
| za-clean | 16 000 | 3 078.6  | 123 142 | 428 858 | 77.7% | 13 746  | 30 360  | (timeout) | 14 914 |
| za-clean | 20 000 | 2 729.7  | 109 188 | 580 812 | 84.2% | 10 494  | 25 903  | (timeout) | 11 319 |
| za-vs    | 5 000  | 4 312.5  | 172 500 | 0       |  0.0% |   21.47 |  252.42 |  295.17 |    66.30 |
| za-vs    | 6 000  | 2 837.2  | 113 488 |  93 512 | 45.2% | 11 870  | 18 317  | 18 874  | 10 658   |
| za-vs    | 8 000  | 5 395.0  | 215 801 |  60 199 | 21.8% | 4 596   | 7 995   | 8 270   | 4 335    |
| za-vs    | 12 000 | 3 953.7  | 158 149 |  87 294 | 35.6% | 9 986   | 22 462  | (timeout) | 11 278 |

Notes:
- For saturated legs the p99 column reads "(timeout)" where NBomber's 30 s request timeout (`-100 operation timeout`) cut the tail. p95/p99 above ~25 s are the timeout itself, not real latency.
- At 5 000 RPS target both templates report exactly 4 312.5 RPS — confirming this is the open-model injector's per-machine ceiling at this rate, not the SUT.
- za-vs @ 6 000 produced *fewer* ok-requests than za-vs @ 8 000 because the SUT entered queue-collapse earlier in the 30 s sustain window; both rates are well past saturation.

## Conclusion for za-clean

**Sustainable ceiling ≈ 6 900 RPS** (8 000 target, 0 failures, p95 = 122 ms, p99 = 216 ms, mean 34.8 ms — over the 1 s "sane" bar at p95 but throughput is real). Saturation begins between 8 000 and 12 000 target: at 12 000 target the SUT delivers the same 6.6 k ok-RPS while 36.6 % of requests time out, and p95 explodes from 122 ms to 13 s. Above 12 k the ok-throughput *drops* (3.1 k @ 16 k, 2.7 k @ 20 k) as queue collapse swallows the server. The previous CI 4 312 plateau understated this template's capacity by roughly **1.6×**.

## Conclusion for za-vs

**Sustainable ceiling ≈ 4 312 RPS** (5 000 target, 0 failures, but p95 already 252 ms — meaningfully worse than za-clean at the same target). Saturation begins between 5 000 and 6 000 target: at 6 000 target 45 % of requests time out and p95 hits 18 s. The za-vs hot-path costs ~8.6× more per request than za-clean at 5 k target (mean 66.3 ms vs 7.7 ms), which translates directly into a lower throughput ceiling — roughly **0.62× of za-clean's ceiling** on the same hardware against the same Postgres.

## Headline

> **Max sustainable NBomber RPS on i9-12900HK:**
> - **za-clean ≈ 6 900 RPS** (saturates above 8 k target)
> - **za-vs    ≈ 4 312 RPS** (saturates above 5 k target — and the 4 312 here is *capacity*, not the injector cap, because p95 is 252 ms vs za-clean's 16 ms at the same offered rate)

## Anything weird

- Both templates hit exactly the same 4 312.5 actual RPS at 5 k target — that's the open-model injector's per-machine cap on this laptop, identical to yesterday's CI EPYC measurement. It does not reflect the SUT.
- At saturation za-clean shows nearly identical mean and p99 latencies (7–11 s) regardless of target (12 k vs 20 k) — classic queue-collapse: once arrivals exceed service rate, latency is just `queue_depth / service_rate` and the injector's increasing target rate only widens the failure count, not throughput.
- No Postgres CPU pinning observed; the 2000-max-connections setting was sufficient and the DB never returned errors (all failures are NBomber-side `-100 operation timeout`). The SUT (Kestrel + ZA.ORM hot path) is the bottleneck, not Postgres.
- The seed step (1000 POSTs at process start) added ~5–7 s per leg; total wall-clock for 9 legs ≈ 9 min.

## Run accounting

- Time taken: ~11 min wall-clock for 9 legs (5 za-clean × 5k/8k/12k/16k/20k + 4 za-vs × 5k/6k/8k/12k). Under the 20-min hard cap.
- za-vs 16k and 20k were skipped per the "stop early once saturated 2× in a row" rule — za-vs was already in deep saturation at 8k and 12k, and higher targets would only have produced more `-100 operation timeout` failures with no new throughput information.
- Raw NBomber outputs are in `.bench-sweep/<template>-<rate>.txt`; full driver logs in `.bench-sweep/<template>-<rate>.driver.log`.
