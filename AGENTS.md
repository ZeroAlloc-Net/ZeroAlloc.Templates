# AGENTS.md — ZeroAlloc.Templates

> For AI coding agents working on the `ZeroAlloc.Templates` repo itself (not on a scaffolded app).

For agents working on a scaffolded `dotnet new za-clean` output, see [content/za-clean/AGENTS.md](content/za-clean/AGENTS.md). The `za-vertical-slice` template has its own [content/za-vertical-slice/AGENTS.md](content/za-vertical-slice/AGENTS.md).

## 1. Repo split

This repo has two distinct surfaces:

| Surface | Path | What lives there |
|---|---|---|
| **Template payload** | `content/za-clean/`, `content/za-vertical-slice/` | The actual scaffolded apps — `src/`, `tests/`, `benchmarks/`, `MyApp.slnx`, plus each template's own `Directory.Build.props` / `Directory.Packages.props` / `global.json`. **Everything under here ships in the NuGet template package.** |
| **Template tooling** | Repo root | `ZeroAlloc.Templates.csproj` (the NuGet template package), `tests/ZeroAlloc.Templates.SmokeTests/` (smoke gate), `.github/workflows/` (CI + release-please), `docs/`, `README.md`. **Does not ship in the template package.** |

The template engine's `sourceName: MyApp` substitution applies to file contents and filenames under `content/<template>/`. The repo root is unaffected.

## 2. Drift-guard for duplicated config

Two files are duplicated between the repo root and each template under `content/` (`za-clean` and `za-vertical-slice`):

- `Directory.Build.props`
- `Directory.Packages.props`

The repo-root copies serve the template-tooling projects (smoke test + template package). The `content/<template>/` copies serve the scaffolded app (so it works standalone after `dotnet new`).

**CI enforces they stay identical** via the drift-guard step in the `build` job of `.github/workflows/ci.yml`, which compares the root copy with both templates. When bumping a package version, **edit all three copies**. CI will refuse to merge if they differ.

`global.json` is deliberately **not** duplicated. The root pins SDK 11 (`11.0.100`), so the smoke tests can target net10.0 and net11.0. Each `content/<template>/global.json` pins SDK 10 (`10.0.404`), the one the scaffolded app targets; it stays on net10.0 until the template offers a framework choice. Bump each pin on its own.

## 3. Verify before commit

Runtimes: the smoke tests multi-target `net10.0` and `net11.0`, so the .NET 10 and .NET 11 runtimes must both be installed. The content test projects target `net10.0` only.

CI commands, by job in `.github/workflows/ci.yml`. The `build-vs` job repeats the `build` content steps for `content/za-vertical-slice/MyApp.slnx`.

```bash
# Job build (and build-vs with the za-vertical-slice solution): content builds + tests pass
dotnet restore content/za-clean/MyApp.slnx
dotnet build content/za-clean/MyApp.slnx --no-restore --configuration Release -warnaserror
dotnet test content/za-clean/MyApp.slnx --no-build --configuration Release

# Job build: primitives benchmark dry-run, from the benchmarks project folder
cd content/za-clean/benchmarks/MyApp.Benchmarks.Primitives
dotnet run -c Release --no-build -- --filter "*" --job dry
cd -

# Job build: template package builds (CI uses -p:Version=0.0.0-ci.N, a version is required)
SHA=$(git rev-parse --short HEAD); dotnet pack ZeroAlloc.Templates.csproj -c Release -p:Version=9.9.9-local.$SHA -o ./nupkg -warnaserror

# Job build: smoke test (install + scaffold + build + test)
dotnet restore tests/ZeroAlloc.Templates.SmokeTests/ZeroAlloc.Templates.SmokeTests.csproj
dotnet test tests/ZeroAlloc.Templates.SmokeTests --configuration Release -warnaserror
```

Skip the slow smoke test during the inner loop; only the smoke tests carry the `Slow` trait, so run just the content solution:

```bash
dotnet test content/za-clean/MyApp.slnx --configuration Release
```

This sequence does not reproduce every CI gate. CI also runs the Coverage gate (`MarcelRoozekrans/ci-baseline/actions/coverage-gate@v1` on the Cobertura reports of the content tests), the `aot-publish-smoke` jobs (scaffold the template and `dotnet publish -r linux-x64 -warnaserror`, boot the binary, run `scripts/request-smoke.sh`) and the `real-run-smoke` jobs (same boot and request smoke on the JIT build), each for both templates. Delete `./nupkg` after a local pack.

Pre-PR checklist:
- The build, test, pack and smoke steps above are green, for both templates
- If you touched the content, the smoke test confirms it still scaffolds + builds
- Drift-guard passes (root vs both `content/<template>/` config files identical)

## 4. Benchmarks

Exception to the org rule that benchmarks run in CI through the shared `benchmark-smoke.yml`: this repo does not call that workflow. `.github/workflows/benchmarks.yml` is manual (`workflow_dispatch`) and runs BenchmarkDotNet and NBomber for both templates; the only per-PR benchmark check is the primitives dry-run in `ci.yml`.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
