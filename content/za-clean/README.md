# MyApp

Clean Architecture Web API scaffolded from `dotnet new za-clean`. Source-generated, zero-allocation through the framework hot path, AOT-safe. Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

## Quickstart

```bash
dotnet run --project src/MyApp.Api
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

The API boots, applies its EF Core SQLite migrations, seeds a sample order in `Development`, and listens on the Kestrel default. OpenTelemetry traces stream to the console.

## Layout

```
src/
├── MyApp.Domain/            Entities, value objects, domain events
├── MyApp.Application/       Commands, queries, handlers, validators (CQRS via ZA.Mediator)
├── MyApp.Infrastructure/    EF Core SQLite, ZA.Rest typed HTTP client + ZA.Resilience
└── MyApp.Api/               Minimal API endpoints, DTOs, JWT auth, OpenTelemetry

tests/
├── MyApp.UnitTests/         xUnit — domain + handler unit tests
├── MyApp.ArchitectureTests/ NetArchTest — boundary rules enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — happy-path + auth roundtrips

benchmarks/
├── MyApp.Benchmarks/        BenchmarkDotNet — per-request cost through full pipeline
└── MyApp.LoadTest/          NBomber — RPS under sustained concurrency
```

## Performance

Measured on a 2022 i9-12900HK / Windows 11 / .NET 10.0.7. Reproduction recipe below.

| Scenario | Mean | p95 | p99 | RPS | Allocated | What it measures |
|---|---:|---:|---:|---:|---:|---|
| `POST /orders` (BDN, in-process) | **2.037 ms** | — | — | — | **156.75 KB** | HTTP → JWT → Mediator → validation → handler → stubbed shipping → EF Core write → 201. Run with no Kestrel via `WebApplicationFactory`. |
| `GET /orders/{id}` (NBomber, real Kestrel) | 1009 ms | 2138 ms | 2634 ms | **473** | — | 500 concurrent VUs for 30s. 14,207 OK / 370 timeouts. SQLite read-bound. |

The BDN's 156.75 KB is dominated by ASP.NET Core's request pipeline (model binding, JSON deserialization, response shaping) and EF Core's tracking buffer — not the ZA framework cost. The handler-level allocation is in the low hundreds of bytes; the rest is HTTP plumbing every endpoint pays.

NBomber's latency under 500-VU load reflects SQLite's single-file lock + EF Core's tracking-context allocation per request. PostgreSQL + EF `AsNoTracking()` on reads + response caching would dramatically improve both throughput and p99.

**Reproduce:**

```bash
# Per-request cost (in-process via WebApplicationFactory, no Kestrel socket)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# Sustained RPS (real Kestrel; stub out the shipping client so the load test
# doesn't DNS-fail against the placeholder shipping URL)
Shipping__UseStub=true dotnet run --project src/MyApp.Api          # terminal 1
dotnet run -c Release --project benchmarks/MyApp.LoadTest          # terminal 2
```

> **`Shipping__UseStub` flag**: the scaffold's shipping client (`IShippingQuoteHttpClient`) targets a placeholder URL (`https://shipping.example/`), so production-shape orders depend on a real endpoint. For load tests, set `Shipping__UseStub=true` (env var) or `Shipping:UseStub: true` (appsettings) — `Program.cs` swaps the real client for an in-memory stub returning a constant `Money(5, "EUR")`. Defaults to `false`; production deployments untouched.

Full methodology + comparison with reflection-based mappers: see [ZeroAlloc.Templates docs/za-clean.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md#benchmarks).

## Extending

- **AI agents**: [AGENTS.md](AGENTS.md) — orientation for Claude Code, Cursor, GitHub Copilot, Codex, Aider. Includes "how to add a command/query/endpoint/value object" recipes and the ZA-specific gotchas.
- **Boundary rules**: enforced by `tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs`. Five NetArchTest rules covering Clean dependency direction.
- **Swap SQLite → PostgreSQL**: change `UseSqlite` to `UseNpgsql` in `Program.cs`, add the EF provider, regenerate migrations. See the template docs for the recipe.

## License

MIT — see [LICENSE](LICENSE).
