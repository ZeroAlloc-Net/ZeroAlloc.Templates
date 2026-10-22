# MyApp

Clean Architecture Web API. **Publishes as a ~27 MB Native AOT binary; cold-starts in ~540 ms** (vs ~1.2 s under JIT — ~2.2× faster, ~75% smaller deploy). Source-generated validators and value objects allocate nothing, which `tests/MyApp.UnitTests/AllocationTests.cs` asserts; mapping, mediator dispatch and data access each have a measured, bounded cost (see the table). Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

| | Value |
|---|---:|
| **AOT binary size** | ~27 MB (`MyApp.Api.exe`) + ~2 MB native deps (win-x64, self-contained) |
| **AOT cold start** | ~540 ms (process → `/healthz` 200, best of 3 on warm disk) |
| **JIT cold start** (comparison) | ~1.2 s, 110 MB total deploy (same scenario) |
| **Framework primitives end-to-end** (map + validate + mediator send) | 472 B = 88 B mapping + 384 B mediator send |
| **Mediator send** (generated `IMediator`, auth + validation behaviours, real handler over stubs) | 384 B = 208 B handler work + 176 B dispatch |
| **Validator (source-generated, regex zip)** | 0 B, 0.09× the time of a mediator send |
| **ValueObject `TryCreate`** | 0 B, 0.01× the time of a mediator send |
| **Read hot path** (`GetByIdAsync` / Sqlite, head + 2 lines) | ZA.ORM 2,992 B vs hand-written ADO.NET 2,128 B — 1.41× the allocations, 1.08× the time. ZA.ORM's own [comparison matrix](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v0.7.0-sqlite-results.md) against hand-written ADO.NET and Dapper.AOT was last captured at ZA.ORM v0.7.0 |
| **End-to-end pipeline** (`POST /orders`, ASP.NET + ZA.ORM) | 36,920 B on SQLite, 38,551 B on Postgres — mostly platform overhead, not ZA |

AOT figures re-measured 2026-06-02 post-ZA.ORM-swap (#152) on i9-12900HK / Windows 11 / .NET 10.0.8. The BenchmarkDotNet rows were re-measured 2026-10-21 on an otherwise idle i9-12900HK / .NET 10.0.13, out of process in three launches pinned to two performance cores; allocations are exact and speed is a same-run ratio. The 88 B mapping cost is the `List<OrderItem>` the command carries, 32 B for the list and 56 B for its one-item array, a caller cost every framework pays. Earlier read-path numbers came from a bench that read the `OrderId(0)` sentinel and so measured the not-found path.

**Reproduce:**

```bash
# Framework primitives (mapping, mediator, validator, value objects in isolation)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks.Primitives -- --filter "*"

# Read hot path (ZA.ORM read-path allocation floor — no HTTP/serialization)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*ReadHotPathBench*"

# Full pipeline (ASP.NET + ZA.ORM — what the platform costs)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# AOT publish + cold-start
dotnet publish src/MyApp.Api -c Release -r win-x64 -o ./aot-out
time ./aot-out/MyApp.Api.exe  # measure to /healthz
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
├── MyApp.Application/       Commands, queries, handlers, validators (CQRS via ZA.Mediator), ports
├── MyApp.Infrastructure/    ZA.ORM partial repositories over IAsyncDbConnection, ZA.Rest typed HTTP client + ZA.Resilience
└── MyApp.Api/               Minimal API endpoints, DTOs, JWT auth, OpenTelemetry

tests/
├── MyApp.UnitTests/         xUnit — domain + handler unit tests
├── MyApp.ArchitectureTests/ NetArchTest — boundary rules enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — happy-path + auth roundtrips

benchmarks/
├── MyApp.Benchmarks.Primitives/ BenchmarkDotNet — ZA primitives in isolation
├── MyApp.Benchmarks/            BenchmarkDotNet — full ASP.NET + ZA.ORM pipeline cost
└── MyApp.LoadTest/              NBomber — RPS under sustained concurrency
```

## Load testing under sustained concurrency

The NBomber load test scenario (read RPS, open-model 5,000-RPS inject for 30s + 10s ramp against real Kestrel, Postgres backend):

| Mean | p50 | p95 | p99 | RPS | Notes |
|---:|---:|---:|---:|---:|---|
| 11 ms | 1.4 ms | 14 ms | 61 ms | **4,312** | 172,500 OK / 0 fail. 2026-06-10 capacity-recipe run on i9-12900HK. |

> ⚠️ **Capacity-recipe local numbers, not capacity claims.** Captured on
> 2026-06-10 via [`docs/benchmarks/capacity-recipe.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/capacity-recipe.md)
> on a single laptop (i9-12900HK, 16 cores) — Postgres pinned to cores 0–1,
> SUT (published `MyApp.Api.exe`) pinned to cores 2–5, NBomber on the host.
> See [`docs/benchmarks/2026-06-10-template-capacity-za-clean.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-10-template-capacity-za-clean.md)
> for the raw NBomber report. The 4,312 RPS plateau is the NBomber injector
> cap on this machine at a 5 k target rate (see [`docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md)
> for the rate-sweep showing actual SUT ceiling ≈ 6,900 RPS), not the SUT's
> sustainable ceiling. Single-machine measurement has known variance — for
> production capacity numbers, run the recipe on your target hardware (the
> two-machine LAN variant in the recipe is the gold-standard).

ZA.ORM has no change tracker — reads materialise straight from `IAsyncDbConnection`, at the 1.41× allocation ratio to hand-written ADO.NET shown above, and the post-#197 output-cache layer absorbs concurrent same-id reads so p99 sits two orders of magnitude below the pre-cache numbers. The harness is shipped so adopters measure on *their* data layer choice and load shape.

**Reproduce:**

```bash
# Sustained RPS (real Kestrel; stub out the shipping client so the load test
# doesn't DNS-fail against the placeholder shipping URL)
Shipping__UseStub=true dotnet run --project src/MyApp.Api          # terminal 1
dotnet run -c Release --project benchmarks/MyApp.LoadTest          # terminal 2
```

> **`Shipping__UseStub` flag**: the scaffold's shipping client (`IShippingQuoteHttpClient`) targets a placeholder URL (`https://shipping.example/`), so production-shape orders depend on a real endpoint. For load tests, set `Shipping__UseStub=true` (env var) or `Shipping:UseStub: true` (appsettings) — `Program.cs` swaps the real client for an in-memory stub returning a constant `Money(5, "EUR")`. Defaults to `false`; production deployments untouched.

Full methodology + per-package comparisons (ZA.Mapping vs Mapperly/AutoMapper, etc.): see [docs/za-clean.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md#benchmarks).

## Output caching

`GET /orders/{id}` is wrapped in ASP.NET Core OutputCaching with a configurable TTL (default 30 seconds). Concurrent same-id reads are served from the in-memory cache, absorbing pressure on the Npgsql connection pool under load — see [docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md) for the empirical investigation that motivated this layer.

**Tuning:**

```json
{
  "OutputCache": {
    "OrderByIdTtlSeconds": 30
  }
}
```

Lower TTL = fresher data + less pool relief; higher = more pool relief + staler responses. A TTL of `0` turns the cache off.

**Eviction:** every cached response is tagged `orders` and `order:{id}`; the GET endpoint adds the per-id tag. `POST /orders` evicts nothing: only 200 responses are cached, so no cached read can describe an order that did not exist yet. An endpoint you add that changes an existing order should evict that order alone, with `EvictByTagAsync($"order:{id}")`. Evicting the whole `orders` tag on every write empties the cache under a steady write rate, and every read then goes back to the database.

**Authenticated GETs:** the framework's `DefaultPolicy` refuses to cache responses with an `Authorization` header. A custom `CacheAuthenticatedGetsPolicy` (in `MyApp.Api/CacheAuthenticatedGetsPolicy.cs`) bypasses that check — safe here because order payloads are not user-specific. If you add an endpoint whose body varies per user, use `.CacheOutput(o => o.SetVaryByHeader("Authorization"))` to key per-token instead.

**Distributed caching:** the default cache is per-process in-memory. Multi-instance deployments need a distributed backing store (Redis is typical via `Microsoft.Extensions.Caching.StackExchangeRedis` + `AddStackExchangeRedisOutputCache`). Out of scope for this template.

## Telemetry

`Program.cs` sends traces and metrics through OpenTelemetry: the console exporter in `Development`, OTLP otherwise. Besides ASP.NET Core, HttpClient and ZA.Mediator spans, it subscribes to the app's own source and meter, named by `Telemetry.SourceName` in `MyApp.Application/Telemetry.cs`.

ZA.Telemetry instruments the shipping port. `[Instrument(Telemetry.SourceName)]` on `IShippingQuoteClient` makes its source generator emit `ShippingQuoteClientInstrumented`, and `AddMyAppInfrastructure` registers that proxy in front of the real `ShippingQuoteClient`. Every quote records a `shipping.quote` span and a `shipping.quote.duration_ms` histogram sample. The proxy is plain generated code, so it is Native AOT safe. `tests/MyApp.IntegrationTests/ShippingTelemetryTests.cs` asserts the registration and both signals. To instrument another interface, add `[Instrument]` plus `[Trace]`, `[Count]` or `[Histogram]` to its methods, and register the generated `<Name>Instrumented` proxy around the implementation.

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

MIT — see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/LICENSE).
