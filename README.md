# ZeroAlloc.Templates

`dotnet new` Clean Architecture Web API template for the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem. Source-generated, AOT-safe, zero-allocation through the framework hot path.

One template ships today — `za-clean` — scaffolding a working Web API + EF Core SQLite + JWT + OpenTelemetry, wired with 10 ZA packages (Mediator, Mapping, Validation, Inject, Rest, Resilience, Authorization, Telemetry, ValueObjects, Results), plus NetArchTest boundary rules and BenchmarkDotNet + NBomber benchmark scaffolds.

## Install

```bash
dotnet new install ZeroAlloc.Templates
```

## Use

```bash
dotnet new za-clean -o MyApp
cd MyApp
dotnet run --project src/MyApp.Api
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

The scaffold ships with:
- Four layered projects (`Domain`, `Application`, `Infrastructure`, `Api`) with NetArchTest-enforced boundaries
- 18 passing tests (10 unit + 5 architecture + 3 integration)
- A BenchmarkDotNet write-pipeline scenario (~2 ms / ~157 KB per request through the full middleware stack on a 2022 i9)
- An NBomber read-RPS scenario for load testing under sustained concurrency
- An `AGENTS.md` that orients Claude Code, Cursor, GitHub Copilot, Codex, and Aider to the codebase

Full tour and rationale: [docs/za-clean.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md).

## What's in the box

| Concern | Package | Where it lives |
|---|---|---|
| CQRS dispatch | ZA.Mediator | `MyApp.Application` (handlers return `ValueTask<T>`) |
| Source-generated mapping | ZA.Mapping | `MyApp.Api/Mappings/` |
| Validation | ZA.Validation (decorative until generator nupkg fix); template ships a hand-rolled validator | `MyApp.Application/CreateOrder/` |
| Source-generated DI | ZA.Inject | `[Scoped]` / `[Singleton]` / `[Transient]` on services |
| Typed HTTP client | ZA.Rest + ZA.Rest.SystemTextJson | `MyApp.Infrastructure/External/` |
| Resilience | ZA.Resilience + ZA.Rest.Resilience | Bridged into the shipping client |
| Smart-ctor IDs/money | ZA.ValueObjects | `MyApp.Domain/ValueObjects/` |
| `Result<T, Error>` | ZA.Results | Across Domain + Application boundaries |

## License

MIT — see [LICENSE](LICENSE).

---

## Development

For maintainers of this repo (not for adopters of the template).

### Required GitHub repo secrets

The release pipeline requires two secrets at `Settings → Secrets and variables → Actions`. Both are configured at org level so they inherit:

- `RELEASE_PLEASE_TOKEN` — Personal Access Token with `contents:write` + `pull-requests:write` + `workflow:write`. Needed because the default `GITHUB_TOKEN` can't trigger downstream workflows from release-please PRs.
- `NUGET_API_KEY` — push-only API key from nuget.org, scoped to `ZeroAlloc.Templates`.

### Drift-guard for duplicated config

Three files are duplicated at the repo root and under `content/za-clean/`:

- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`

The root copies serve the template-tooling projects; the `content/` copies serve the scaffolded app standalone. **CI enforces they stay identical** via a drift-guard step. When bumping a package version or SDK pin, edit both copies.

### Build + test

```bash
dotnet build content/za-clean/MyApp.slnx
dotnet test content/za-clean/MyApp.slnx
dotnet pack ZeroAlloc.Templates.csproj -o ./nupkg

# Full smoke test (install + scaffold + build + test, ~25 s):
dotnet test tests/ZeroAlloc.Templates.SmokeTests

# Skip the slow smoke test during inner-loop dev:
dotnet test --filter "Category!=Slow"
```

See [AGENTS.md](AGENTS.md) for the AI-agent orientation.
