# Benchmark documentation

This folder holds benchmark reports — BenchmarkDotNet `.md` exports and NBomber run summaries — plus the runbooks for producing them.

## What the benches measure

The project has three categories of benchmark, each answering a different question.

### 1. BDN micro-benches (per-call CPU + allocation)

Single-threaded, deterministic. Run in-process against in-memory backends. Measure the cost of one isolated operation (a mediator dispatch, a security-context read, an ORM materialization) with `MemoryDiagnoser` reporting allocations per call.

**Use to answer:** "How much does this operation cost on the hot path?" — alloc bytes, ns per op.
**Examples in this folder:** `2026-06-04-za-clean-sec-context-alloc.md`, `2026-06-04-flat-read-model-alloc.md`.

### 2. HTTP-level BDN (single request through the full stack)

BDN benchmarks that boot a `WebApplicationFactory<Program>` SUT and fire one HTTP request per iteration. Kestrel, JWT, JSON serialization, mediator, repo, response — all included. Single-request semantics (no concurrency).

**Use to answer:** "What's the per-request CPU + alloc cost end-to-end, ignoring concurrency effects?"
**Examples in this folder:** none yet — the first HTTP-level BDN reports will land with [#189](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/189) investigation results.

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
| 2026-06-05 | [nbomber-ceiling-sweep.md](2026-06-05-nbomber-ceiling-sweep.md) | NBomber capacity | Laptop rate-sweep finding actual SUT ceilings — za-clean sustains ~6.9k RPS; za-vs collapses just above 5k target (see [#189](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/189)) |

### Runbooks (not reports)

| File | Purpose |
|---|---|
| [capacity-recipe.md](capacity-recipe.md) | Step-by-step recipe for the decoupled local capacity bench (Docker CPU pinning + Windows / Linux / two-machine variants). |
