# Capacity Bench Recipe + Doc Reframe — Design

**Status:** approved 2026-06-08
**Scope:** ZA.Templates docs-only. No CI infrastructure changes, no benchmark code changes.
**Target version:** ZA.Templates v0.14.1 (patch — `docs:` doesn't bump under release-please, but the README footnote edit may; `docs:` squash keeps it patch-or-no-bump).
**Closes:** [#170](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/170)
**Branch:** `docs/170-bench-capacity-recipe` off `main` at the post-#187 commit.

## Background

Issue #170 documents that the CI NBomber jobs (`nbomber-postgres-vs`, `nbomber-postgres-clean` in `.github/workflows/benchmarks.yml`) run the load generator, the SUT, and Postgres on the same GitHub-hosted runner (~2–4 vCPUs). Empirical proof from the latest CI run at 5k target:

- actual RPS = 4,312
- p50 = 188.8 ms
- **min = 0.55 ms**

The 0.55 ms minimum proves the SUT can serve an uncontended request in half a millisecond. The 188 ms p50 is overwhelmingly queueing — NBomber, Kestrel, and Postgres fighting for the same cores. The 4,312 RPS plateau seen on both templates at 5k target is the **open-model NBomber injector's per-machine cap**, not SUT capacity. Confirmed independently on 2026-06-05 by the laptop rate-sweep (`docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md`), which found:
- za-clean: **~6,900 RPS sustainable** (saturates between 8k and 12k)
- za-vs: ~4,312 RPS sustainable (just above the injector cap at 5k target)

CI is currently reporting tainted numbers, and there is no doc anywhere telling readers that.

Issue acceptance:
- [ ] A decoupled-generator benchmark profile exists and is documented.
- [ ] The docs distinguish "regression net" (co-located) from "capacity" (decoupled) numbers.

## Decision

Adopt **Approach D** from the brainstorm: ship a documented local recipe for capacity testing, frame the existing CI workflow as a regression net, and update inline comments + per-template README footnotes to point readers at the right doc. **No new CI infrastructure, no benchmark code.**

Rejected:
- **Self-hosted runners / paid GHA larger runners** — no infra budget, project is open-source/template work.
- **Cloudflared-tunnel between two GHA jobs** — tunnel latency (10-50 ms RTT) would dominate the measurement we're trying to make, defeating the point.
- **Adding an autoscan `LOADTEST_TARGET_RPS=0` mode** — the existing manual sweep recipe is sufficient; YAGNI.

## What changes

**Files created (2):**

1. **`docs/benchmarks/README.md`** *(new, ~80 lines)* — top-level entry point for the `docs/benchmarks/` folder. Three sections:
   - *What the benches measure* — BDN micro-benches (per-call CPU/alloc), HTTP-level BDN (single-request full stack, no concurrency), NBomber (load with concurrency).
   - *Regression-net vs capacity* — the load-bearing distinction. Cites the 2026-06-05 sweep doc as empirical evidence that the co-located CI NBomber underreports capacity by ~1.6×.
   - *Index* — links each report file in `docs/benchmarks/` with a one-line annotation of what it measured + which category.
2. **`docs/benchmarks/capacity-recipe.md`** *(new, ~150 lines)* — runbook for the local capacity bench. Single-laptop variant first (recommended), two-machine LAN appendix.

**Files modified (3):**

3. **`.github/workflows/benchmarks.yml`** — replace the inline comments in `nbomber-postgres-vs` and `nbomber-postgres-clean` that say *"For a true single-box ceiling, run the generator on a separate machine and raise LOADTEST_TARGET_RPS to 10000"* with a pointer to `docs/benchmarks/capacity-recipe.md`. Two ~3-line comment edits, no behavior change.
4. **`content/za-clean/README.md`** — add a footnote near any documented RPS number pointing at `docs/benchmarks/README.md` so readers know it's a regression-net figure, not a capacity claim. **Do NOT change the numbers themselves** — that's separate follow-up work that needs a fresh capacity-recipe run.
5. **`content/za-vertical-slice/README.md`** — same footnote pattern.

## Single-laptop recipe shape

The recipe in `capacity-recipe.md` will spell out, for the most common case (Windows/Linux laptop with Docker Desktop or Docker Engine):

```
1. Hardware floor: 8+ cores. Bigger is better — pinning only works if you have spare cores.
2. Postgres pinned to cores 0-1 via:
     docker run -d --rm --name pg-cap --cpuset-cpus="0-1" \
       -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
       -p 5432:5432 postgres:17 -c max_connections=2000
3. Build SUT in Release.
4. Start SUT pinned to cores 2-5:
   - Windows: Start-Process powershell -ProcessorAffinity 0x3C ...
   - Linux:   taskset -c 2-5 dotnet run ...
   - Cross-platform: docker run --cpuset-cpus="2-5" myapp:latest
     (requires a Dockerfile for the SUT — flag this if it doesn't exist)
5. NBomber runs on the host without CPU pinning. The host scheduler routes the
   load generator to whichever cores aren't pinned (6-15 on a 16-core box).
6. Sweep LOADTEST_TARGET_RPS in 5000/8000/12000/16000 across two templates.
7. Capture each NBomber report — save under docs/benchmarks/ with a date prefix.
8. Compare to the 2026-06-05 sweep numbers as a sanity check.
9. Cleanup: docker stop pg-cap.
```

## Two-machine LAN variant (appendix)

For maintainers with a second physical machine on the same LAN:

```
Host A (SUT + Postgres):
   - Docker run postgres as above.
   - dotnet run -c Release --project src/MyApp(.Api) -- --urls http://0.0.0.0:5000

Host B (NBomber):
   - dotnet run -c Release --project benchmarks/MyApp.LoadTest -- http://<host-a>.local:5000

Caveats:
   - LAN RTT contributes directly to per-request mean. Only viable on sub-1ms RTT.
   - WiFi-to-WiFi typically not viable (3-15 ms RTT swallows the signal).
```

## Tests + acceptance

- The recipe **must be run end-to-end on the maintainer's laptop once** before merge. A docs PR that says "do X" without proving X works is anti-value. The implementation plan will include a verification step that runs the recipe at 5k target on both templates and confirms the report shapes match the 2026-06-05 sweep.
- All existing tests stay green (this PR doesn't touch test code).
- CI workflows still pass — the inline comment edits don't change CI behavior.

## Versioning

- `docs(bench):` commit — docs-only addition.
- Squash title at merge: `docs(bench): decoupled-generator capacity recipe + framing (#170)`.
- release-please mapping: `docs:` → no version bump (correct — this isn't a feature/fix/perf change to the template content itself).

## What stays out of scope

- New benchmark code (e.g. autoscan mode in the LoadTest binary). The existing manual rate-sweep is sufficient.
- Changes to the CI workflow's RPS targets, topology, or budgets.
- Authoring a Dockerfile for the SUT if one doesn't exist. The recipe will mention the Docker path as one option among multiple; if the maintainer wants to add a Dockerfile later, that's a separate PR.
- Updating the actual RPS claims in template READMEs. Per the design, this PR only adds a footnote disclaimer. Replacing the numbers themselves requires running the recipe and is follow-up work.
- A `docs/benchmarks/README.md` index that auto-updates. Manual maintenance for now.

## Risk

- **The recipe might not work first-try on every platform.** Windows process affinity, Linux taskset, and Docker `--cpuset-cpus` all have edge cases. Mitigation: implementation runs the recipe and irons out the platform-specific notes before merge. If a platform variant turns out to be flaky, document the limitation rather than over-engineering a workaround.
- **The "single-laptop variant" still has shared-memory, GC, and L3-cache contention between containers and the host process.** This is a real residual noise source that no amount of CPU pinning will eliminate. Document it honestly — the recipe gets us "much closer to capacity" but isn't gold-standard. Gold standard requires two physical machines.
- **Footnote drift.** If the RPS numbers in template READMEs are eventually replaced with capacity numbers, the footnote wording will need updating. Acceptable — the footnote is the cheap fix; updating numbers comes when someone has fresh data.
