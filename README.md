# ZeroAlloc.Templates

`dotnet new` Web API templates for the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem. Source-generated, AOT-safe, zero-allocation through the framework hot path.

## Catalog

| Short name | Architecture | When to pick it |
|---|---|---|
| `za-clean` | Clean Architecture — four layered projects (Domain / Application / Infrastructure / Api) with NetArchTest boundary rules | Conservative shape; familiar to teams already running layered architectures; horizontal boundaries lock concerns in place |
| `za-vertical-slice` | Vertical Slice Architecture — one src project, one folder per use case, one file per slice (request + validator + handler + endpoint + entity co-located) | Lower-ceremony shape; each feature reads top-to-bottom in one ~50–100 line file; sharper A/B comparison against `za-clean` on the same domain |

Both templates ship the same 11-package showcase (Mediator, Validation, Authorization, Mapping, ValueObjects, Inject, Telemetry, Resilience, Rest, Results, ORM), same ZA.ORM 1.1 + SQLite persistence (raw `Microsoft.Data.Sqlite` provider, no DbContext), same xUnit + BenchmarkDotNet + NBomber test/bench fan-out. They differ in **arrangement**, not in functional scope — pick on team comfort, not capability.

## Install

```bash
dotnet new install ZeroAlloc.Templates
```

## Use

```bash
# Clean Architecture
dotnet new za-clean -o MyApp
cd MyApp && dotnet run --project src/MyApp.Api

# Vertical Slice
dotnet new za-vertical-slice -o MyApp
cd MyApp && dotnet run --project src/MyApp

# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

`za-clean` ships with:
- Four layered projects (`Domain`, `Application`, `Infrastructure`, `Api`) with NetArchTest-enforced boundaries
- 18 passing tests (10 unit + 5 architecture + 3 integration)
- A BenchmarkDotNet write-pipeline scenario (~2 ms / ~157 KB per request through the full middleware stack on a 2022 i9)
- An NBomber read-RPS scenario for load testing under sustained concurrency
- An `AGENTS.md` that orients Claude Code, Cursor, GitHub Copilot, Codex, and Aider to the codebase

`za-vertical-slice` ships with:
- One src project (`MyApp`) — no horizontal layers; concerns live next to the feature that uses them
- Six slices (PlaceOrder, GetOrder, ListOrders, CancelOrder, CreateCustomer, GetCustomer) with request + validator + handler + endpoint + entity co-located per `*.cs` file
- 33 passing tests (16 unit + 12 integration + 5 NetArchTest convention rules)
- The same BenchmarkDotNet + NBomber benchmark fan-out adapted to the flat `PlaceOrderCommand` shape

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
| Source-generated persistence | ZA.ORM 1.1 + ZA.AdoNet.Async | `[Command]`/`[Query]` partials over `IAsyncDbConnection`; embedded SQL migrations under `Persistence/Migrations/` |

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

Three files are duplicated at the repo root and under each `content/<template>/`:

- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`

The root copies serve the template-tooling projects; the `content/` copies serve the scaffolded app standalone. **CI enforces they stay identical** across the root and every `content/<template>/` directory via a drift-guard step. When bumping a package version or SDK pin, edit all copies.

### Build + test

```bash
# Per-template (replace za-clean ↔ za-vertical-slice as needed):
dotnet build content/za-clean/MyApp.slnx
dotnet test content/za-clean/MyApp.slnx
dotnet pack ZeroAlloc.Templates.csproj -o ./nupkg

# Full smoke test (parameterized over both templates: install + scaffold + build + test, ~50 s):
dotnet test tests/ZeroAlloc.Templates.SmokeTests

# Skip the slow smoke tests during inner-loop dev:
dotnet test --filter "Category!=Slow"
```

See [AGENTS.md](AGENTS.md) for the AI-agent orientation.
