# ZeroAlloc.Templates

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Templates.svg)](https://www.nuget.org/packages/ZeroAlloc.Templates)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

`dotnet new` Web API templates for the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem. Source-generated and published as Native AOT. The ZeroAlloc primitives — value objects, `Result<T, E>`, generated validators — allocate nothing per call, and each template's README publishes the measured cost of every framework step above them.

## Catalog

| Short name | Architecture | When to pick it |
|---|---|---|
| `za-clean` | Clean Architecture — four layered projects (Domain / Application / Infrastructure / Api) with NetArchTest boundary rules | Conservative shape; familiar to teams already running layered architectures; horizontal boundaries lock concerns in place |
| `za-vertical-slice` | Vertical Slice Architecture — one src project, one folder per use case, one file per slice (request + validator + handler + endpoint + entity co-located) | Lower-ceremony shape; each feature reads top-to-bottom in one ~50–100 line file; sharper A/B comparison against `za-clean` on the same domain |

Both templates reference the same 12 ZeroAlloc libraries (Mediator, Validation, Authorization, Mapping, ValueObjects, Inject, Resilience, Rest, Results, Serialisation, ORM, Telemetry). Both share the same ZA.ORM 2 + SQLite persistence (raw `Microsoft.Data.Sqlite` provider, no DbContext), same xUnit + BenchmarkDotNet + NBomber test/bench fan-out. They differ in **arrangement**, not in functional scope — pick on team comfort, not capability.

## Install

```bash
dotnet new install ZeroAlloc.Templates
```

## Use

```bash
# Clean Architecture
dotnet new za-clean -o MyApp
cd MyApp && dotnet run --project src/MyApp.Api
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}

# Vertical Slice
dotnet new za-vertical-slice -o MyApp
cd MyApp && dotnet run --project src/MyApp
# In another shell:
curl http://localhost:5000/healthz
# → Healthy
```

`za-clean` ships with:
- Four layered projects (`Domain`, `Application`, `Infrastructure`, `Api`) with NetArchTest-enforced boundaries
- 49 passing tests (12 unit + 5 architecture + 32 integration)
- A BenchmarkDotNet write-pipeline scenario (~1.3 ms / ~36 KB per request through the full middleware stack on Linux/AMD EPYC Postgres CI; see [run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747))
- An NBomber read-RPS scenario for load testing under sustained concurrency
- An `AGENTS.md` that orients Claude Code, Cursor, GitHub Copilot, Codex, and Aider to the codebase

`za-vertical-slice` ships with:
- One src project (`MyApp`) — no horizontal layers; concerns live next to the feature that uses them
- Six slices (PlaceOrder, GetOrder, ListOrders, CancelOrder, CreateCustomer, GetCustomer) with request + validator + handler + endpoint + entity co-located per `*.cs` file
- 70 passing tests (37 unit + 28 integration + 5 NetArchTest convention cases over 4 rules)
- The same BenchmarkDotNet + NBomber benchmark fan-out adapted to the flat `PlaceOrderCommand` shape

Full tour and rationale: [docs/za-clean.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md) and [docs/za-vertical-slice.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-vertical-slice.md).

## What's in the box

Paths are for `za-clean`; `za-vertical-slice` keeps the same pieces next to the slice that uses them.

| Concern | Package | Where it lives |
|---|---|---|
| CQRS dispatch | ZA.Mediator | `MyApp.Application` (handlers return `ValueTask<T>`) |
| Source-generated mapping | ZA.Mapping | `MyApp.Api/Mappings/` |
| Validation | ZA.Validation + ZA.Mediator.Validation | `[Validate]` on the command; `MyApp.Application/CreateOrder/CreateOrderValidator.cs` maps the generated validator's first failure to a `UnitResult<ValidationError>` |
| Source-generated DI | ZA.Inject | `[Scoped]` / `[Singleton]` / `[Transient]` on services |
| Typed HTTP client | ZA.Rest + ZA.Rest.SystemTextJson | `MyApp.Infrastructure/External/` |
| Resilience | ZA.Resilience + ZA.Rest.Resilience | Bridged into the shipping client |
| Smart-ctor IDs/money | ZA.ValueObjects | `MyApp.Domain/ValueObjects/` |
| `Result<T, Error>` | ZA.Results | Across Domain + Application boundaries |
| Authorization | ZA.Authorization + ZA.Mediator.Authorization | `[RequirePolicy]` on commands; policies in `MyApp.Application/Authorization/` |
| JSON for value objects | ZA.Serialisation + ZA.Serialisation.SystemTextJson | Generated `JsonConverter`s for the `[ValueObject]` ids, registered with `AddZeroAllocValueObjectConverters()` in `MyApp.Api/Program.cs` |
| Instrumentation | ZA.Telemetry | `[Instrument]` on `MyApp.Application/IShippingQuoteClient.cs`; `AddMyAppInfrastructure` registers the generated proxy. `za-vertical-slice` instruments the CreateCustomer slice's `ICustomerWriter` |
| Source-generated persistence | ZA.ORM 2 + AdoNet.Async | `[Command]`/`[Query]` partials over `IAsyncDbConnection`; embedded SQL migrations under `Persistence/Migrations/` |

## License

MIT — see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/LICENSE).

Maintaining this repo? See [CONTRIBUTING.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/CONTRIBUTING.md).

