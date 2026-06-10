# za-clean — capacity-recipe run (2026-06-10)

- **Date / hardware**: 2026-06-10, laptop i9-12900HK (16 cores), Windows, Docker Desktop.
- **Variant**: single-laptop ([`docs/benchmarks/capacity-recipe.md`](capacity-recipe.md)). Postgres pinned to cores 0–1 (`--cpuset-cpus="0-1"`), SUT (`MyApp.Api.exe` published binary) pinned to cores 2–5 (`ProcessorAffinity = 0x3C`), NBomber on host with no pinning.
- **Stack**: post-#197 output caching; SUT scenario `GET /orders/{id}` benefits from the `OrderByIdTtlSeconds=30` cache layer.
- **Workload**: NBomber open-model inject, target 5,000 RPS, 10 s ramp + 30 s sustain. The 4,312.5 actual RPS reflects NBomber's per-machine injector cap at 5 k target (see capacity-recipe.md §Caveats), not SUT saturation.

---

> test info



test suite: `nbomber_default_test_suite_name`

test name: `nbomber_default_test_name`

session id: `2026-06-10_10-26-34_a50d6d0a`

> scenario stats



scenario: `read_order_by_id`

  - ok count: `172500`

  - fail count: `0`

  - all data: `0` MB

  - duration: `00:00:40`

load simulations:

  - `ramping_inject`, rate: `5000`, interval: `00:00:01`, during: `00:00:10`

  - `inject`, rate: `5000`, interval: `00:00:01`, during: `00:00:30`

|step|ok stats|
|---|---|
|name|`global information`|
|request count|all = `172500`, ok = `172500`, RPS = `4312.5`|
|latency (ms)|min = `0.15`, mean = `11.18`, max = `3742.34`, StdDev = `128.18`|
|latency percentile (ms)|p50 = `1.41`, p75 = `2.84`, p95 = `14.33`, p99 = `61.31`|
