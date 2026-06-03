# MyApp

Clean Architecture Web API. **Publishes as a ~27 MB Native AOT binary; cold-starts in ~540 ms** (vs ~1.2 s under JIT — ~2.2× faster, ~75% smaller deploy). Source-generated, zero-allocation through the validator + mediator + mapping chain (data-access adds provider-shaped overhead — see benchmarks table). Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

| | Value |
|---|---:|
| **AOT binary size** | ~27 MB (`MyApp.Api.exe`) + ~2 MB native deps (win-x64, self-contained) |
| **AOT cold start** | ~540 ms (process → `/healthz` 200, best of 3 on warm disk) |
| **JIT cold start** (comparison) | ~1.2 s, 110 MB total deploy (same scenario) |
| **Framework primitives end-to-end** | ~125 ns / 160 B (= mapping cost alone; chain adds 0 B) |
| **Mediator dispatch alone** | ~31 ns / 0 B |
| **Validator (source-generated, regex zip)** | ~57 ns / 0 B |
| **ValueObject `TryCreate`** | ~3 ns / 0 B |
| **Read hot path** (`GetByIdAsync` / Sqlite, head + 2 lines) | ZA.ORM ~27 µs / 1.71 KB vs hand-written ADO.NET ~26 µs / 1.57 KB — framework 1.09× allocations (+0.14 KB AdoNet.Async wrapper overhead). See [ZA.ORM v0.7.0 benchmarks](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) for the full comparison matrix |
| **End-to-end pipeline** (ASP.NET + ZA.ORM, Postgres) | ~1.3 ms / 36 KB — mostly platform overhead, not ZA |

AOT figures re-measured 2026-06-02 post-ZA.ORM-swap (#152) on i9-12900HK / Windows 11 / .NET 10.0.8. Pipeline + primitive numbers measured in CI on Ubuntu 24.04 / AMD EPYC / .NET 10.0.8 via [`Benchmarks (manual)` run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747). The decisive datapoint: the validator + Mediator dispatch through the chain allocate **zero bytes**. The 160 B is the `CreateOrderCommand` record + nested `OrderItem[]` array, a caller cost every framework pays.

**Reproduce:**

```bash
# Framework primitives (zero-alloc, ns/op — what ZA is)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks.Primitives -- --filter "*"

# Read hot path (ZA.ORM read-path allocation floor — no HTTP/serialization)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"

# Full pipeline (ASP.NET + ZA.ORM — what the platform costs)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# AOT publish + cold-start
dotnet publish src/MyApp.Api -c Release -r win-x64 -o ./aot-out
time ./aot-out/MyApp.Api  # measure to /healthz
```

## Quickstart

```bash
dotnet run --project src/MyApp.Api
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

The API boots, applies its ZA.ORM-managed embedded SQL migrations, seeds a sample order in `Development`, and listens on the Kestrel default. OpenTelemetry traces stream to the console.

## Layout

```
src/
├── MyApp.Domain/            Entities, value objects, domain events
├── MyApp.Application/       Commands, queries, handlers, validators (CQRS via ZA.Mediator)
├── MyApp.Infrastructure/    ZA.ORM partial repositories over IAsyncDbConnection, ZA.Rest typed HTTP client + ZA.Resilience
└── MyApp.Api/               Minimal API endpoints, DTOs, JWT auth, OpenTelemetry

tests/
├── MyApp.UnitTests/         xUnit — domain + handler unit tests
├── MyApp.ArchitectureTests/ NetArchTest — boundary rules enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — happy-path + auth roundtrips

benchmarks/
├── MyApp.Benchmarks.Primitives/ BenchmarkDotNet — ZA primitives in isolation (0 B framework cost)
├── MyApp.Benchmarks/            BenchmarkDotNet — full ASP.NET + ZA.ORM pipeline cost
└── MyApp.LoadTest/              NBomber — RPS under sustained concurrency
```

## Load testing under sustained concurrency

The NBomber load test scenario (read RPS, open-model 5,000-RPS inject for 30s + 10s ramp against real Kestrel, Postgres backend):

| Mean | p50 | p95 | p99 | RPS | Notes |
|---:|---:|---:|---:|---:|---|
| 247 ms | 189 ms | 679 ms | 1,137 ms | **4,312** | 172,500 OK / 0 fail. Captured in CI on AMD EPYC 7763. |

ZA.ORM has no change tracker — reads materialise straight from `IAsyncDbConnection` with zero overhead; Postgres + open-model inject sustains 4.3k RPS with zero failures. The wider tail vs the EF-era closed-model baseline reflects the load-shape change (open-model `Inject` removes the closed-loop backpressure that previously bounded p99). The harness is shipped so adopters measure on *their* data layer choice and load shape.

**Reproduce:**

```bash
# Sustained RPS (real Kestrel; stub out the shipping client so the load test
# doesn't DNS-fail against the placeholder shipping URL)
Shipping__UseStub=true dotnet run --project src/MyApp.Api          # terminal 1
dotnet run -c Release --project benchmarks/MyApp.LoadTest          # terminal 2
```

> **`Shipping__UseStub` flag**: the scaffold's shipping client (`IShippingQuoteHttpClient`) targets a placeholder URL (`https://shipping.example/`), so production-shape orders depend on a real endpoint. For load tests, set `Shipping__UseStub=true` (env var) or `Shipping:UseStub: true` (appsettings) — `Program.cs` swaps the real client for an in-memory stub returning a constant `Money(5, "EUR")`. Defaults to `false`; production deployments untouched.

Full methodology + per-package comparisons (ZA.Mapping vs Mapperly/AutoMapper, etc.): see [docs/za-clean.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md#benchmarks).

## Extending

- **AI agents**: [AGENTS.md](AGENTS.md) — orientation for Claude Code, Cursor, GitHub Copilot, Codex, Aider. Includes "how to add a command/query/endpoint/value object" recipes and the ZA-specific gotchas.
- **Boundary rules**: enforced by `tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs`. Five NetArchTest rules covering Clean dependency direction.
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. AOT-correct: ZA.ORM's `MigrationRunner` picks up the embedded SQL files under `src/MyApp.Infrastructure/Persistence/Migrations/Postgres/` on startup — no reflection, no design-time tooling. After entity or schema changes, hand-author a new migration file under both providers:
  ```
  src/MyApp.Infrastructure/Persistence/Migrations/Sqlite/002_add_customer_email.sql
  src/MyApp.Infrastructure/Persistence/Migrations/Postgres/002_add_customer_email.sql
  ```
  File-naming convention: `NNN_description.sql` — a 3+ digit zero-padded version prefix (strictly increasing) plus a snake_case description. `MigrationRunner` orders by the prefix and applies anything newer than the recorded high-water mark on next startup.

## License

MIT — see [LICENSE](LICENSE).
