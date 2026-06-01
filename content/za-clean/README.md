# MyApp

Clean Architecture Web API. **Publishes as a 35.8 MB Native AOT single-file binary; cold-starts in ~1.0 s** (vs ~2.2 s under JIT — ~2.2× faster). Source-generated, zero-allocation through the framework hot path. Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

| | Value |
|---|---:|
| **AOT binary size** | 35.8 MB single-file, self-contained (win-x64) |
| **AOT cold start** | ~1.0 s (process → `/healthz` 200, best of 3) |
| **JIT cold start** (comparison) | ~2.2 s (same scenario) |
| **Framework primitives end-to-end** | ~165 ns / 200 B (= mapping cost alone; chain adds 0 B) |
| **Mediator dispatch alone** | ~37 ns / 0 B |
| **Validator (hand-rolled, regex zip)** | ~40 ns / 0 B |
| **ValueObject `TryCreate`** | ~13 ns / 0 B |
| **End-to-end pipeline** (ASP.NET + ZA.ORM) \* | 156 KB / 2 ms — mostly platform overhead, not ZA |

\* Numbers captured pre-ZA.ORM-swap; re-capture pending.

Measured on a 2022 i9-12900HK / Windows 11 / .NET 10.0.7. The decisive datapoint: `EndToEndPrimitives` matches `Mapping_RequestToCommand` byte-for-byte — the validator + Mediator dispatch through the chain allocate **zero bytes**. The 200 B is the `CreateOrderCommand` record + nested `OrderItem[]` array, a caller cost every framework pays.

**Reproduce:**

```bash
# Framework primitives (zero-alloc, ns/op — what ZA is)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks.Primitives -- --filter "*"

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

The NBomber load test scenario (read RPS, 500 VUs for 30s against real Kestrel):

| Mean | p95 | p99 | RPS | Notes |
|---:|---:|---:|---:|---|
| 1009 ms | 2138 ms | 2634 ms | **473** | 14,207 OK / 370 timeouts. SQLite read-bound. |

> Numbers captured pre-ZA.ORM-swap; refresh tracked in backlog.

Latency under 500-VU load reflects SQLite's single-file lock. ZA.ORM has no change tracker — reads materialise straight from `IAsyncDbConnection` with zero overhead. PostgreSQL + response caching dramatically improve both throughput and p99 — the harness is shipped so adopters measure on *their* data layer choice.

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
