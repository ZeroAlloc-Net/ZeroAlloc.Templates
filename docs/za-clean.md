---
id: za-clean
title: ZeroAlloc Clean Architecture Template
description: Tour of `dotnet new za-clean` — what it scaffolds, how the layers fit, where each ZA package lives.
sidebar_position: 1
---

# ZeroAlloc Clean Architecture Template

`za-clean` is a `dotnet new` template that scaffolds a four-project Clean Architecture solution (Domain / Application / Infrastructure / Api) pre-wired with the ZeroAlloc package family: source-generated mediator, DI registration, REST client, resilience proxy, and OpenTelemetry. The goal is to skip the first day of plumbing — start a new service and have a running endpoint, an integration test, a benchmark harness, and architecture-boundary tests within a single `dotnet build`.

This page is a tour, not a reference. Each package links out to its own docs for depth.

## Quickstart

Three terminals' worth of setup from "nothing installed" to "I can see it work".

```bash
dotnet new install ZeroAlloc.Templates
dotnet new za-clean -o MyApp
cd MyApp && dotnet run --project src/MyApp.Api
```

The Api boots, applies its embedded SQL migrations via `ZeroAlloc.ORM.Migrations.MigrationRunner` (SQLite by default), seeds a sample order in `Development`, and listens on the kestrel default. In another shell:

```bash
$ curl http://localhost:5000/healthz
{"status":"ok"}
```

The same process is emitting OpenTelemetry traces to the console — `GET /healthz` produces a single `Microsoft.AspNetCore` span, and any subsequent `POST /orders` will produce a nested trace covering the mediator handler, the ZA.ORM-emitted ADO.NET commands (one INSERT per row, plus the read-back), and the `shipping.quote` span the ZA.Telemetry proxy records around the shipping client, with its outbound HTTP call nested inside.

## Tour of the Layers

The four projects under `src/` follow the standard Clean Architecture dependency direction: Api → Infrastructure → Application → Domain, with Application defining the abstractions Infrastructure implements.

```
HTTP POST /orders
        │
        ▼
┌──────────────────────────────┐
│ MyApp.Api                    │  Endpoint → maps DTO → IMediator.Send
│  Endpoints/OrdersEndpoints   │
└──────────┬───────────────────┘
           ▼
┌──────────────────────────────┐
│ MyApp.Application            │  CreateOrderHandler : IRequestHandler<,>
│  CreateOrder/                │  validates → calls IOrderRepository,
│                              │             IShippingQuoteClient
│                              │  ([Instrument]: ZA.Telemetry proxy)
└──────────┬───────────────────┘
           ▼
┌──────────────────────────────┐
│ MyApp.Infrastructure         │  OrderRepository (ZA.ORM partial over
│  Persistence/, External/     │   IAsyncDbConnection; SQLite or Npgsql)
│                              │  ShippingQuoteHttpClient (ZA.Rest proxy)
└──────────┬───────────────────┘
           ▼
┌──────────────────────────────┐
│ MyApp.Domain                 │  Order, OrderLine, Money, OrderStatus
│                              │  Smart constructors, no deps.
└──────────────────────────────┘
```

**Domain** holds `Order`, `OrderLine`, `OrderStatus`, and the `Money` / `Sku` value objects. It has zero references to anything outside itself — no persistence, no ASP.NET, no ZA packages other than ZA.Results for `Result<T, DomainError>`. Invariants live in smart constructors.

**Application** holds the CQRS slice: `CreateOrderCommand`/`Handler`, `GetOrderByIdQuery`/`Handler`, the `IOrderRepository` and `IShippingQuoteClient` abstractions, and `ApplicationError`. `IShippingQuoteClient` carries ZA.Telemetry's `[Instrument]`, so the generator emits the `ShippingQuoteClientInstrumented` tracing proxy alongside it. Handlers implement `IRequestHandler<TRequest, TResponse>` from ZA.Mediator and return `ValueTask<Result<T, ApplicationError>>`. A hand-rolled `CreateOrderValidator` runs at the top of `CreateOrderHandler.Handle` and short-circuits to a `validation.failed` `ApplicationError` on the first invalid field — intentional: one error per response keeps the template's API simple, and ZA.Validation's batched form will land here when its generator nupkg ships.

**Infrastructure** holds `OrderRepository` — a `public sealed partial class OrderRepository(IAsyncDbConnection conn)` with `[Query]`/`[Command]` partial methods that the ZA.ORM source generator fills in against raw `Microsoft.Data.Sqlite` / `Npgsql` providers, the embedded SQL migrations under `Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` applied at startup by `ZeroAlloc.ORM.Migrations.MigrationRunner`, the static `MoneyConverter.ToStorage` / `FromStorage` helper used to round-trip value objects through TEXT columns, and `ShippingQuoteHttpClient` — a `[ZeroAllocRestClient]` interface that ZA.Rest + ZA.Resilience compose into a typed HTTP client with retry/timeout policies. `InfrastructureServiceCollectionExtensions.AddMyAppInfrastructure(...)` wires it all up, registers `IAsyncDbConnection` as scoped, and resolves `IShippingQuoteClient` to the telemetry proxy around `ShippingQuoteClient`, the adapter over the typed HTTP client.

**Api** holds `Program.cs`, the minimal-API endpoint groups, DTOs, and DTO ↔ domain mappings (ZA.Mapping). It composes the other layers, registers JWT auth + the `OrdersRead`/`OrdersWrite` policies, and configures OpenTelemetry.

## Each ZA Package's Role

The template references the ZeroAlloc.* package family plus the `AdoNet.Async` async-ADO.NET wrapper that ZA.ORM builds on. Generators ship as separate `*.Generator` nupkgs and are wired with `PrivateAssets=all` so they don't transit to downstream consumers — relevant when you add more ZA packages later.

| Package | Where it lives in the template | Notes |
| --- | --- | --- |
| [ZA.Results](https://results.zeroalloc.net) | Domain (`Result<T, DomainError>`), Application (`Result<T, ApplicationError>`) | Smart-constructor failures surface as structured `Result`. |
| [ZA.Mapping](https://mapping.zeroalloc.net) | `MyApp.Api/Mappings/` | `[Map<DTO, Domain>]` static partial classes. Zero-alloc happy path. |
| [ZA.Validation](https://validation.zeroalloc.net) | `MyApp.Application/CreateOrder/CreateOrderCommand.cs`, `OrderItem.cs` | `[Validate]` attributes on the command + nested item record. ZA.Validation's source generator emits `CreateOrderCommandValidator` and `OrderItemValidator` at build time. `[NotEmpty]` on `IReadOnlyList<OrderItem>` covers the "at least one item" rule via the type-aware emission introduced in 1.3.0. The thin facade at [`CreateOrderValidator.cs`](../content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderValidator.cs) maps the first failure to `ApplicationError("validation.failed", ...)` so `CreateOrderHandler.Handle` stays unchanged. |
| [ZA.Mediator](https://mediator.zeroalloc.net) | All handlers in `MyApp.Application/*` | `IRequest<TResponse>` / `IRequestHandler<TRequest, TResponse>`. Handlers return `ValueTask<T>`. ActivitySource `ZeroAlloc.Mediator` is wired into OTel. |
| [ZA.Inject](https://inject.zeroalloc.net) | `OrderRepository`, `ShippingQuoteClient`, all handlers | `[Scoped]` / `[Singleton]` / `[Transient]` attributes — **not** `[Service(ServiceLifetime.X)]`. Generated `AddMyAppApplication()` / `AddMyAppInfrastructure(...)` extensions compose registration. |
| [ZA.Authorization](https://authorization.zeroalloc.net) + [ZA.Mediator.Authorization](https://mediator.zeroalloc.net) | `MyApp.Application/Authorization/OrdersPolicies.cs`, `MyApp.Api/Authorization/HttpSecurityContextAccessor.cs` | `[AuthorizationPolicy("OrdersRead"/"OrdersWrite")]` defines two policies that read the JWT `scope` claim (RFC 6749 space-separated tokens). Commands and queries carry `[Authorize(...)]`. The mediator pipeline behavior runs the policy against an `ISecurityContext` bridged from `HttpContext.User` and denies dispatch before the handler executes — **defense in depth on top of** the endpoint-level `RequireAuthorization` policies (same names, same claims) registered on the minimal-API routes. |
| [ZA.Telemetry](https://telemetry.zeroalloc.net) | `MyApp.Application/IShippingQuoteClient.cs` | `[Instrument(Telemetry.SourceName)]` on the shipping port, with `[Trace("shipping.quote")]` and `[Histogram("shipping.quote.duration_ms")]` on `GetQuoteAsync`. The generator emits the internal `ShippingQuoteClientInstrumented` proxy; `AddMyAppInfrastructure` registers it in front of `ShippingQuoteClient`, and `Program.cs` subscribes to the source and meter with `AddSource` / `AddMeter`. Plain generated code, Native AOT safe. |
| [ZA.Rest](https://rest.zeroalloc.net) | `MyApp.Infrastructure/External/IShippingQuoteHttpClient.cs` | `[ZeroAllocRestClient]` on the interface. Generates `services.AddIShippingQuoteHttpClient(opts => opts.BaseAddress = ...)`. |
| [ZA.Resilience](https://resilience.zeroalloc.net) | Same interface, alongside Rest | `[Retry]` / `[Timeout]` attributes on the interface. Generates a proxy type that wraps the inner client. |
| [ZA.Rest.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest.Resilience) | Bridge package | Composes the Rest + Resilience generators so a single attribute-decorated interface yields both an HTTP client *and* a resilience proxy. |
| [ZA.ORM](https://orm.zeroalloc.net) | `MyApp.Infrastructure/Persistence/OrderRepository.cs` | The runtime side of ZA.ORM. The repository is a `public sealed partial class OrderRepository(IAsyncDbConnection conn)` whose `[Query]` / `[Command]` partial method declarations the generator fills in with hand-shaped ADO.NET. Connection lifecycle is ref-counted — the emitted body calls `OpenAsync()` only if the connection isn't already open, and closes only what it opened. No DbContext, no change tracker. |
| [ZA.ORM.Abstractions](https://orm.zeroalloc.net) | Referenced by both Infrastructure and Application | Holds `IAsyncDbConnection` adapters and the `[Query]` / `[Command]` / `[StoredProcedure]` attributes. Application doesn't take a dependency on the concrete provider, only on the abstraction. |
| [ZA.ORM.Generator](https://orm.zeroalloc.net) | Analyzer reference in `MyApp.Infrastructure.csproj` | Emits the INSERT / SELECT / multi-result-set bodies behind the `[Query]` / `[Command]` partials. Ships as a separate `*.Generator` nupkg, wired with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`. |
| [AdoNet.Async](https://github.com/ZeroAlloc-Net/AdoNet.Async) | `MyApp.Infrastructure` | The `IAsyncDbConnection` / `IAsyncDbCommand` abstraction layer over raw ADO.NET. Lets ZA.ORM target Sqlite / Npgsql / any future provider through one shape without taking a DbContext-style dependency. |
| [AdoNet.Async.Adapters](https://github.com/ZeroAlloc-Net/AdoNet.Async) | `InfrastructureServiceCollectionExtensions` | Provides the `Microsoft.Data.Sqlite` / `Npgsql` -> `IAsyncDbConnection` adapter registrations. Scoped per request via `services.AddScoped<IAsyncDbConnection>(...)`. |

## Boundary Tests

`tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` runs four NetArchTest rules against the four assemblies. All pass on a fresh scaffold:

1. `Domain_does_not_depend_on_anything_outside_Domain` — no ASP.NET, no Application/Infrastructure/Api. (The rule's namespace ban list still names `Microsoft.EntityFrameworkCore` as a defensive guard against future EF re-introduction; the swap left it in place.)
2. `Application_does_not_depend_on_Infrastructure_or_Api` — and no ASP.NET either.
3. `Infrastructure_does_not_depend_on_Api` — keeps composition one-directional.
4. `Handlers_live_in_Application_only` — `IRequestHandler<,>` implementations cannot leak into Domain/Infrastructure/Api.

Each rule is `[Fact]`-shaped and uses NetArchTest's fluent API:

```csharp
[Fact]
public void Domain_does_not_depend_on_anything_outside_Domain()
{
    var result = Types.InAssembly(Domain)
        .Should()
        .NotHaveDependencyOnAny(
            "MyApp.Application", "MyApp.Infrastructure", "MyApp.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
        .GetResult();
    Assert.True(result.IsSuccessful, FormatFailure(result));
}
```

Extending the set is a copy-paste-rename. Common additions: ban `System.Reflection` outside generated code, require all command/query records to live under `MyApp.Application`, or assert that all public endpoint classes end in `Endpoints`.

## Benchmarks

Three harnesses, three questions.

**`MyApp.Benchmarks.Primitives` (BenchmarkDotNet, in-isolation)** — `PrimitivesBench` exercises each ZA layer standalone: mapping, mediator dispatch, the handler alone, validator, value-object construction, and an end-to-end chain. No ASP.NET, no database, no HTTP. Mediator dispatch goes through the real source-generated `IMediator` from `AddMyAppApplication`, authorization and validation behaviours included, into the real `CreateOrderHandler` over zero-work stubs. The validator and value objects are 0 B; mapping, the handler and the mediator are not, and the table says by how much.

**`MyApp.Benchmarks` (BenchmarkDotNet, in-process)** — `WritePipelineBench` hosts the API via `WebApplicationFactory<Program>` and measures `POST /orders` end-to-end through middleware, model binding, mediator dispatch, validation, the ZA.ORM `[Command]`-generated INSERT path against `IAsyncDbConnection`, and the outbound shipping call (stubbed). It reports allocation per request and the time on Postgres relative to SQLite. `ReadPipelineBench` sends `GET /orders/{id}` the same way, once with output caching switched off for the route, so the request runs the mediator, the ZA.ORM read and JSON serialisation, and once served from the output cache. `JwtValidationBench` measures JWT bearer validation alone, for [#171](benchmarks/2026-08-07-171-jwt-validation-cost.md). In-process means you're measuring the *pipeline*, not the network — useful for spotting regressions, not capacity planning.

**`MyApp.LoadTest` (NBomber, real Kestrel)** — drives sustained concurrency against a real Kestrel process. Two terminals: one runs the Api, the other runs the load test. NBomber reports p50/p95/p99 latency and RPS. This is where you size your service.

### AOT publish

| | Value |
|---|---:|
| AOT binary size (win-x64, self-contained, single file) | 35.8 MB |
| AOT cold start (process → `/healthz` 200, best of 3) | ~1.0 s |
| JIT cold start (same scenario, best of 3) | ~2.2 s |
| AOT speedup | ~2.2× faster cold start |

Captured on .NET 10.0.7 / 2022 i9-12900HK / Windows 11. The template's `MyApp.Api.csproj` defaults to `<PublishAot>true</PublishAot>`. The ZA.ORM swap removed the previous EF Core-anchored AOT blockers — the persistence layer is now plain source-generated ADO.NET on top of `Microsoft.Data.Sqlite` / `Npgsql`, and CI's `aot-publish-smoke` job verifies `dotnet publish -p:PublishAot=true -r linux-x64` produces a working binary that boots and answers `/healthz`. JSON serialization uses `JsonContext` source-gen. `InvariantGlobalization=true` keeps the binary lean; adopters needing culture-sensitive parsing should set it to `false` and document the ICU dependency.

Reproduce:

```bash
dotnet publish src/MyApp.Api -c Release -r win-x64 -o ./aot-out
./aot-out/MyApp.Api  # serves on :5000
```

A caveat on the storage layer: the template ships SQLite-in-WAL because it's frictionless to scaffold. Read-heavy benchmarks are honest — WAL handles concurrent readers well. **Write-heavy benchmarks need PostgreSQL** for production-grade numbers; SQLite serialises writers and will under-report what your real stack can do. (The Mapperly LINQ-fallback comparison from ZA.Mapping's benchmark suite isn't relevant here — the template's BDN measures *its own* pipeline, not a vs-comparison.)

### Results

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Measured 2026-10-21 on an otherwise idle machine, out of process, in three launches pinned to two
performance cores. Ratios come from one run, with the lowest and highest of the three launches in
brackets. Postgres ran in a local Docker container.

The BenchmarkDotNet tables below come from that run. The NBomber tables come from [run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747) of the `Benchmarks (manual)` workflow, the first end-to-end capture **after** the EF Core → ZA.ORM 1.1 swap landed in PR #152. Pre-swap tables further down (`(historical, pre-swap)`) are preserved for diff-over-time context.

#### Primitives — ZA framework cost in isolation

Measured with `MyApp.Benchmarks.Primitives` — no ASP.NET, no database, just the ZA packages and the template's own handler.

Each row's time relative to `Mediator_Send`:

| Method | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| `Mapping_RequestToCommand` | 0.04 | 0.04–0.04 | 88 B |
| `Mediator_Send` | 1.00 | | 384 B |
| `Handler_Direct` | 0.30 | 0.29–0.31 | 208 B |
| `Validator_Generated` | 0.09 | 0.09–0.09 | 0 B |
| `ValueObject_TryCreate` | 0.01 | 0.01–0.01 | 0 B |
| `EndToEndPrimitives` | 1.15 | 1.10–1.16 | 472 B |

Earlier versions of this table reported `Mediator_DispatchOnly` at 0 B and `EndToEndPrimitives` at 0 B. Both were wrong: the dispatch row called a hand-written `FakeMediator`, not ZA.Mediator, and the end-to-end row allocated 160 B per call, as its own Gen0 column showed. The 2026-10-11 capture measured 160 B for mapping and 248 B for dispatch; the ZA.Mapping and ZA.Mediator releases since then brought them down.

What the bytes are:

- `Mapping_RequestToCommand`, 88 B: the `List<OrderItem>` the command carries, 32 B for the list and 56 B for its one-item array. Every framework pays this caller cost; ZA.Mapping does not add to it.
- `Handler_Direct`, 208 B: `CreateOrderHandler` building the `Order` aggregate, its line list and the line record. This is application code, not framework cost.
- `Mediator_Send`, 384 B: the handler's 208 B plus 176 B for the generated mediator's dispatch through the authorization and validation behaviours.
- `EndToEndPrimitives`, 472 B: mapping 88 B plus the mediator send 384 B.
- `Validator_Generated` and `ValueObject_TryCreate` allocate nothing.

`Validator_Generated` measures the ZA.Validation-generated `CreateOrderCommandValidator` end-to-end, including the regex check on `ShippingZip`. As of [ZA.Validation 1.5.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/52), `[Matches]` emits a `private static readonly Regex` field initialised with `RegexOptions.Compiled` — JIT-compiled matcher, no per-call cache lookup, direct dispatch.

Compare with the full-pipeline `WritePipeline` row below: that 36,911 B is ASP.NET model binding + JSON + the ZA.ORM command round-trip, not ZA framework cost. Use the primitives table for "does the framework allocate", the pipeline table for "does the endpoint allocate".

#### Full pipeline (ASP.NET + ZA.ORM) — `POST /orders`

| Method | Backend | Ratio | Launch range | Allocated |
|---|---|---:|---:|---:|
| `WritePipeline` | Sqlite | 1.00 | | 36,911 B |
| `WritePipeline` | Postgres | 38.4 | | 38,467 B |

Re-measured 2026-10-22, after both templates turned on Npgsql auto-prepare and multiplexing by default,
in three launches pinned to two performance cores. Allocations moved by under 0.3%. The ratio was
37.1 the day before; it divides by an in-memory SQLite run measured in the same session, so it also moves
when SQLite does. Over real HTTP with one client the Postgres write latency did not change; see
[the capacity report](benchmarks/2026-10-22-template-capacity.md).

The ~37 KB per request is dominated by ASP.NET Core's request pipeline (model binding, JSON deserialization, response shaping) plus the ZA.ORM `[Command]`-generated SQL round-trip — not the framework hot path. **Allocations match within 1.7 KB across backends** — the framework cost is provider-independent; the gap is the Npgsql command/parameter cost vs Microsoft.Data.Sqlite's lighter wire. Time on Postgres is dominated by the round trip to the database container, so treat the ratio as a property of this setup, not of the template.

#### Full pipeline (ASP.NET + ZA.ORM) — `GET /orders/{id}`

Measured in a second run the same evening, together with `JwtValidationBench`. Ratio is each row's
time relative to the uncached request.

| Method | Output cache | Ratio | Launch range | Allocated |
|---|---|---:|---:|---:|
| `ReadPipeline` | off | 1.00 | | 28,738 B |
| `ReadPipeline` | hit | 0.57 | 0.54–0.58 | 24,723 B |

The benchmark repeats one request for one id, and the route caches authenticated GETs. Before
2026-10-21 it had no uncached variant, so every result after the first iteration was a cache hit:
the earlier `ReadPipeline` figures, including the denominator of the #171 report, measure the
cached path. A cache hit still runs the middleware, authentication and the output-cache lookup,
which is why it costs more than half of a full read.

**EF Core → ZA.ORM 1.1 delta, from the CI runs (vs the historical table further down).** Run 26778623747 measured, post-swap, Sqlite: 928.6 μs → 221.7 μs (−76%) / 148.4 KB → 32.6 KB (−78%). Postgres: 1,729.7 μs → 1,253.6 μs (−28%) / 147.4 KB → 36.3 KB (−75%). The relative time-win is much larger on Sqlite because Postgres latency is dominated by the network round-trip — the framework-overhead share that's left after the wire is still ~76% leaner. The allocation profile reflects ZA.ORM having no change tracker, no model snapshot, no proxy materialisation: the `[Command]` source generator emits a direct `IAsyncDbConnection.ExecuteAsync` against the embedded SQL.

#### Full pipeline (ASP.NET + EF Core in the mix) — pre-swap baseline (historical)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS
AMD EPYC 7763, .NET SDK 10.0.300, .NET 10.0.8 X64 RyuJIT x86-64-v3
```

| Method        | Backend  | Mean       | Error     | StdDev    | Allocated |
|-------------- |--------- |-----------:|----------:|----------:|----------:|
| WritePipeline | Sqlite   |   928.6 μs | 23.58 μs  | 63.36 μs  | 148.41 KB |
| WritePipeline | Postgres | 1,729.7 μs | 33.83 μs  | 43.99 μs  | 147.43 KB |

The ~148 KB per request was dominated by ASP.NET Core's request pipeline (model binding, JSON deserialization, response shaping) and EF Core's tracking buffer — not the ZA framework cost. The handler-level allocation (mapping + Mediator dispatch + Result construction) sat in the low hundreds of bytes; the rest was HTTP plumbing every endpoint paid. **Allocations matched within 1 KB across backends** — the framework cost was already provider-independent. Postgres was ~85% slower per request than in-memory SQLite (real I/O + WAL + per-statement network), but the absolute allocation budget didn't shift. This row is kept for diff-over-time context; the post-swap table above is the current baseline.

#### NBomber — read-RPS scenario (real Kestrel, Postgres) — post-swap, [run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747)

> **Superseded for capacity:** [2026-10-22-template-capacity.md](benchmarks/2026-10-22-template-capacity.md) measures both
> templates with `oha` on separate cores, so the load generator is not the limit, and explains what
> changed since. The numbers below are kept for history.

`GET /orders/{id}` driven by NBomber's open-model `Inject(rate=5000/s)` for 30 s + 10 s ramp, against a real Kestrel SUT backed by Postgres (`max_connections=500`, `Maximum Pool Size=500`), after seeding 1000 orders.

| Metric | Value |
|---|---:|
| OK / fail | **172,500 / 0** |
| RPS (ok) | **4,312.5** |
| Latency min / mean / max | 0.55 ms / 247.25 ms / 2,297.28 ms |
| StdDev | 234.02 ms |
| p50 / p75 / p95 / p99 | 188.8 / 318.0 / 678.9 / 1,136.6 ms |

172,500 ok / 0 fail at 4,312 sustained RPS, p50 188.8 ms, p99 1,136.64 ms under open-model 5k-RPS inject. Compared to the EF era's closed-model 500-VU baseline (only Sqlite was captured for za-clean, at 473 RPS), this is an order-of-magnitude move — Postgres + ZA.ORM lets the same hardware absorb 9× the read rate. The wider tail vs the EF-era 500-VU run reflects the load-shape change (open-model `Inject` removes the closed-loop backpressure that previously bounded p99); apples-to-apples on a 500-VU closed-model harness would compress p99 substantially.

The scenario's purpose isn't to publish a benchmark; it's to give adopters a working harness pointed at a representative endpoint so the first thing they do on a new branch is run it and see how their changes moved the needle.

#### NBomber — read-RPS scenario (real Kestrel, file-SQLite) — pre-swap baseline (historical)

`GET /orders/{id}` at 500 concurrent VUs for 30 seconds, after seeding 1000 orders. API run with `Shipping__UseStub=true` so the seed step bypasses the placeholder shipping endpoint (the config flag landed in v0.2.1 — see Customising below).

| Metric | Value |
|---|---:|
| OK / fail | **14,207 / 370** |
| RPS (ok) | **473.57** |
| Mean latency | 1009 ms |
| p50 / p75 / p95 / p99 | 887 / 1203 / 2138 / 2634 ms |
| Failure mode | operation timeout (370 / 14,577 = 2.5%) |

These reflected an unoptimised file-SQLite-backed scaffold under heavy concurrency. The 1 s mean / 2 s p95 latency was dominated by SQLite's single-file lock combined with EF Core's tracking-context allocation per request. This row is kept for diff-over-time context; the post-swap Postgres table above is the current baseline.

### Comparisons

Per-primitive comparisons against the ecosystem alternatives. These blocks are refreshed by `tools/import-comparisons.ps1`; raw markdown comes from each ZA package's own benchmark harness, ensuring per-package perf claims and the template's claims stay in sync.

#### Mapping
<!-- MAPPING:START -->
_Imported from ZA.Mapping — last refreshed 2026-10-22._

_Last refreshed: 2026-10-17_

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  Job-WGASOM : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Affinity=00000000000000010100  LaunchCount=3  

```

> Allocations are exact. Speed is shown as same-run ratios against the hand-written mapper; absolute timings are not published.

### FlatIdentity

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 72 B |
| ZeroAlloc_ | 0.96 | 72 B |
| Mapperly_ | 0.98 | 72 B |
| AutoMapper_ | 3.93 | 72 B |

### FlatConversion

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 48 B |
| ZeroAlloc_ | 0.97 | 48 B |
| Mapperly_ | 0.93 | 48 B |
| AutoMapper_ | 1.28 | 48 B |

### Flattening

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 72 B |
| ZeroAlloc_ | 0.98 | 72 B |
| Mapperly_ | 0.98 | 72 B |
| AutoMapper_ | 4.34 | 72 B |

### Collection

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 78.18 KB |
| ZeroAlloc_ | 1.03 | 78.18 KB |
| Mapperly_ | 1.25 | 78.25 KB |
| AutoMapper_ | 1.57 | 86.52 KB |

### Polymorphic

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 32 B |
| ZeroAlloc_ | 1.00 | 32 B |
| Mapperly_ | 0.96 | 32 B |
| AutoMapper_ | 5.67 | 32 B |

### UpdateInPlace

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | - |
| ZeroAlloc_ | 0.88 | - |
| Mapperly_ | 0.98 | - |
| AutoMapper_ | 6.10 | - |

### TryMap

| Method | Ratio | Allocated |
|---|---:|---:|
| HandWritten_ | 1.00 | 48 B |
| ZeroAlloc_ | 1.01 | 48 B |
| Mapperly_ | 0.96 | 48 B |
<!-- MAPPING:END -->

#### Mediator
<!-- MEDIATOR:START -->
_Imported from ZA.Mediator — last refreshed 2026-10-22._

| Operation | MediatR time | ZA Alloc | MediatR Alloc |
|---|---:|---:|---:|
| Send | **3.89×** (3.54–4.30) | 0 B | 224 B |
| Publish (1 handler) | **16.36×** (15.79–17.43) | 0 B | 792 B |
| Publish (multi handler) | **20.41×** (20.12–20.72) | 0 B | 1,032 B |
| Send + Pipeline | **4.24×** (3.59–4.80) | 0 B | 152 B |
| Send (via IMediator DI, scope per call) | **1.48×** (1.45–1.50) | 176 B | 496 B |
| Send + Pipeline (via IMediator DI, scope per call) | **1.37×** (1.35–1.39) | 176 B | 352 B |
| Publish, 1 handler (via IMediator DI, scope per call) | **2.69×** (2.64–2.78) | 176 B | 992 B |
| Publish, multi handler (via IMediator DI, scope per call) | **3.20×** (3.11–3.34) | 200 B | 1,232 B |
| Stream (5 items) | **2.77×** (2.73–2.83) | 104 B | 528 B |
| Stream (via IMediator DI, scope per call) | **2.25×** (2.21–2.29) | 280 B | 728 B |

The static `Mediator` class is **3.9–20× faster** than MediatR, with zero heap allocations on every non-streaming path. An earlier run reported 160× for `Send`, from a 0.5 ns figure for the static call, too short to measure reliably; the ratio above comes from three pinned launches. `HotPathAllocationTests` and `SpanExceptionDescriptionTests` gate `Send`, `Send` + pipeline and `Publish` at 0 B, and `HotPathAllocationTests` also gates `Send` and `Publish` with no factory registered.

**Handler fallback.** These rows use handlers with a parameterless constructor and no registered factory. The static `Mediator` then reuses one cached instance per handler type, provided the handler is stateless: no instance fields, auto-properties, field-like events or primary-constructor parameters, on the class or on any base class, and no base class from another assembly, whose private fields the generator cannot see. Static fields and constants do not count. The cache is filled on first use and published with `Interlocked.CompareExchange`, so every caller shares one instance; it does not rely on the JIT, so the 0 B holds on .NET 8 and before tier-up too. Before [#328](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/328) the fallback created the handler on every call, 24 B for an empty class, and these rows read 0 B only because the .NET 10 JIT stack-allocated it.

A handler that holds state is still created on every call, so state never leaks from one call into the next; that is one handler object per call. `Mediator.Configure(c => c.SetFactory(() => handler))` is only needed for such stateful handlers, when you want them shared, or 0 B. A registered factory always takes precedence over the cached instance.

**IMediator allocation sources.** The two DI rows create a scope and resolve `IMediator` from it on every call, the way an ASP.NET Core request does, so they include the scope and the transient `MediatorService`. On top of that, `IMediator.Send` with pipeline behaviors can allocate one `next` delegate per behavior per call: its lambdas read the provider it was resolved from, so the compiler cannot cache them (ZeroAlloc.Pipeline#121 tracks removing this). The benchmark's behavior only forwards to `next`, so the .NET 10 JIT inlines it and keeps the delegate on the stack; a behavior that awaits, such as the authorization bridge, pays 64 B per behavior. A pipeline with a behavior that may read `PipelineBehaviorServices.Current` adds 72 B per call for that `AsyncLocal` write; the benchmark's behavior declares `UsesPipelineBehaviorServices = false` and the bridge behaviors take the provider as an argument, so they skip it. See [How a bridge reaches your container](dependency-injection.md#how-a-bridge-reaches-your-container).

**Stream allocation source.** The 104 B on the ZA Stream row is the C# compiler's `async IAsyncEnumerable<T>` state-machine allocation for the user's handler method (e.g. `async IAsyncEnumerable<int> Handle(...) { yield return ...; }`). Any mediator dispatching to such handlers pays this cost — MediatR pays it *on top of* its own ~424 B wrapper. ZA.Mediator's own stream-dispatch contribution is 0 B. .NET 10 has no pooled state-machine builder for `async IAsyncEnumerable<T>` (unlike `ValueTask` with `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]`); the only way to drop below 104 B is to implement `IAsyncEnumerable<T>` by hand without `yield return`.
<!-- MEDIATOR:END -->

#### Validation
<!-- VALIDATION:START -->
_Imported from ZA.Validation — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21, against FluentValidation 12.2.0. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published. Speedups are FluentValidation's mean divided by ZeroAlloc's, from the same BenchmarkDotNet run._

| Scenario | Speedup (valid) | Speedup (invalid) | ZA alloc (valid) | FV alloc (valid) | ZA alloc (invalid) | FV alloc (invalid) |
|---|---:|---:|---:|---:|---:|---:|
| Flat | **22.7×** | 68.8× | **0 B** | 664 B | 120 B | 5,408 B |
| Nested | **100.3×** | 20.9× | **0 B** | 1,488 B | 488 B | 6,328 B |
| Collection (3 items) | **145.0×** | 25.5× | **0 B** | 3,528 B | 592 B | 11,640 B |
| Precision and enum | **61.1×** | 129.0× | **0 B** | 816 B | 88 B | 6,824 B |

ZeroAlloc.Validation is **22.7–145.0× faster** than FluentValidation 12 on the valid path with **zero heap allocation**. On the invalid path it is 20.9–129.0× faster and allocates 13.0–77.5× less. FluentValidation allocates on every call, valid or not, for its per-call `List<ValidationFailure>` and validation context.
<!-- VALIDATION:END -->

#### Inject
<!-- INJECT:START -->
_Imported from ZA.Inject — last refreshed 2026-10-22._

_Last refreshed: 2026-10-17_

| Method | Ratio | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 1.00 | 4,384 B |
| ZA.Inject Container — `BuildZeroAllocInjectServiceProvider()` | **0.67** (0.63–0.74) | **2,720 B** |
| ZA.Inject Standalone — `new …StandaloneServiceProvider()` | **0.01** (0.01–0.01) | 112 B |
| Jab — `new JabContainer()` | <0.01 | **40 B** |

`BuildZeroAllocInjectServiceProvider()` snapshots the `IServiceCollection` and **lazy-builds the MS DI fallback `IServiceProvider` on first need** via `Interlocked.CompareExchange`. Applications whose registrations are fully ZA-owned (the common case for `[Singleton]`/`[Transient]`/`[Scoped]`-attributed services) never pay the `BuildServiceProvider()` cost at all. The standalone provider and Jab are plain constructor calls, two orders of magnitude cheaper still.

## Resolution Benchmarks

| Scenario | MS DI | ZA Container | ZA Standalone | Jab | Allocated |
|---|---:|---:|---:|---:|---:|
| Transient (no deps) | 1.00 | 0.62 (0.54–0.68) | 0.58 (0.51–0.69) | **0.30** (0.25–0.36) | 24 B |
| Transient (1 dep) | 1.00 | 0.98 (0.87–1.11) | 0.92 (0.83–1.02) | **0.54** (0.52–0.55) | 48 B |
| Transient (1 property dep) | 1.00 | **0.45** (0.43–0.47) | **0.45** (0.44–0.46) | N/A¹ | 48 B |
| Transient (2 deps) | 1.00 | 1.09 (1.03–1.13) | 1.14 (1.07–1.21) | **0.62** (0.57–0.67) | 104 B |
| Singleton | 1.00 | 0.85 (0.74–1.03) | 0.83 (0.71–1.10) | **0.19** (0.15–0.21) | 0 B |
| Decorated transient | 1.00 | 0.59 (0.57–0.62) | 0.56 (0.51–0.60) | **0.26**² (0.23–0.28) | 48 B |
| `IEnumerable<T>` (3 impls) | 1.00 | 0.96 (0.88–1.01) | 0.90 (0.75–0.99) | **0.62** (0.50–0.70) | 168 B |
| Open generic (closed type) | **1.00** | 1.12 (1.07–1.21) | 1.08 (1.07–1.09) | N/A³ | 24 B |
| Create scope | 1.00 / 128 B | 1.09 (1.05–1.13) / 96 B | 0.85 (0.84–0.86) / 88 B | **0.16** (0.16–0.17) / **40 B** | — |
| Resolve scoped, first in a new scope⁴ | 1.00 / 304 B | 0.66 (0.59–0.78) / 120 B | **0.54** (0.49–0.63) / 120 B | **0.54** (0.52–0.57) / 120 B | — |
| Resolve scoped, cached⁵ | 1.00 | 0.23 (0.22–0.24) | 0.22 (0.21–0.23) | **0.04** (0.03–0.04) | 0 B |

_¹ Jab is constructor-only — no property injection._
_² Jab decorator wired via factory (no first-class decorator attribute)._
_³ Jab 0.10.x requires closed types at the `[ServiceProvider]` attribute level._
_⁴ One resolve per fresh scope: BenchmarkDotNet runs one invocation per iteration, so this is the first, creating resolve, and the scope itself is created outside the measurement. The 120 B is the benchmark's `ScopedService`, a 24 B object plus its 96 B Guid string; ZA and Jab add nothing to it, MS DI adds 184 B._
_⁵ A service already resolved in a scope created in `[GlobalSetup]`._

On .NET 10, **Jab is the fastest container in every scenario it supports**, and the lightest on scope creation. ZA.Inject's clear wins over MS DI are where the generator's domain knowledge matters: property injection (2.2× faster), decorators (1.7×), cached scoped resolves (4.3×) and the first resolve in a new scope (1.5–1.9×), all of which also allocate no more than MS DI. With two dependencies, for open generics and, in hybrid mode, for scope creation, ZA is up to 14% slower than MS DI.

The **ZA.Inject Container** creates the MS DI fallback scope only on first fallback resolution, via `Interlocked.CompareExchange`. For applications whose registrations are fully ZA-owned (the common case), the fallback scope is never created at all, and creating a scope does not build the MS DI fallback provider either: it is built only if something asks for a service no registration provides. **`IEnumerable<T>` resolutions are also cached** when every entry is `Singleton`: `EnumerableCacheTests` checks that the same array comes back, and `ResolutionAllocationTests` asserts 0 B for repeat resolutions in both modes; the benchmark row above shows the all-`Transient` registration path, which remains genuinely allocation-bound — the cache fires for the singleton-multi-impl scenario.
<!-- INJECT:END -->

#### Results
<!-- RESULTS:START -->
_Imported from ZA.Results — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

**The realistic-workload headline (100 iterations with 1-in-3 failures),** each library's time relative to ZeroAlloc.Results in the same run:

| Library | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| ZeroAlloc.Results | **1.00** | | **0 B** |
| OneOf | 1.02 | 1.01–1.03 | 0 B |
| ErrorOr | 4.14 | 4.10–4.27 | 2,992 B |
| FluentResults | 41.39 | 40.10–42.43 | 25,968 B |

**Single operations,** allocated bytes per call:

| Scenario | ZeroAlloc.Results | OneOf | ErrorOr | FluentResults |
|---|---:|---:|---:|---:|
| Success construct | 0 B | 0 B | 0 B | **112 B** |
| Failure construct | 0 B | 0 B | **88 B** | **272 B** |
| Success consume | 0 B | 0 B | 0 B | **96 B** |
| Failure consume | 0 B | 0 B | 0 B | **240 B** |

Times for the single operations are not given for ZeroAlloc.Results, OneOf and ErrorOr: each takes well under a nanosecond, below what BenchmarkDotNet resolves, because the JIT reduces constructing or reading a small struct to almost nothing. The operations that allocate are measurable: FluentResults' failure construct takes about 4.5 times as long as ErrorOr's in the same run.

ZeroAlloc.Results and OneOf both allocate **0 B on every path**, including failure construction, which is where ErrorOr (88 B) and FluentResults (272 B) pay the most. Both are struct-based and run at parity on the mixed workload.

ErrorOr and FluentResults allocate per-failure because their error types (`Error` struct with description string interning + `IError` interface implementations) are non-trivial. For a CRUD app handling occasional validation errors the cost is invisible; for a hot pipeline processing tens of thousands of items where any non-trivial fraction fail, it dominates.
<!-- RESULTS:END -->

#### ValueObjects
<!-- VALUEOBJECTS:START -->
_Imported from ZA.ValueObjects — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

| Operation | Vogen | ZA.ValueObjects |
|---|---:|---:|
| `From(value)` | below resolution | below resolution |
| `Equals` (equal) | below resolution | below resolution |
| `Equals` (not equal) | below resolution | below resolution |
| `GetHashCode` | below resolution | below resolution |
| `ToString` | 1.00 | 1.09 (0.90–1.42) |

Both libraries allocate 0 B on every row. For `ToString` that holds only because the benchmark formats 42, and .NET caches the strings of small integers; formatting a larger value returns a new string.

Every row except `ToString` takes well under a nanosecond for both libraries, below what BenchmarkDotNet resolves: the JIT reduces wrapping, comparing or hashing a single `int` to almost nothing, so no ratio is given. `ToString` is at parity within the launch spread. The May 2026 capture reported ZeroAlloc 12–15 times faster on `From` and `Equals`; those ratios came from sub-nanosecond figures and do not hold up with three pinned launches.

**The trade-off**: on a single wrapped primitive the two libraries perform the same. Vogen's narrower wrapping (single primitive) and ZA's broader surface (multi-field, custom types, EF Core converters) make them complementary choices — pick by feature surface, not raw single-int benchmark numbers.

History: the previous single-property `ToString` allocated ~72 B per call and `GetHashCode` was ~30× slower than Vogen, both fixed in ZeroAlloc.ValueObjects v1.7 by emitting bare `Value.ToString(InvariantCulture)` / `Value.GetHashCode()` for 1-property `[ValueObject]` types. Multi-property types are unchanged.
<!-- VALUEOBJECTS:END -->

#### Specification
<!-- SPECIFICATION:START -->
_Imported from ZA.Specification — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21 — Ardalis.Specification 9.3.1, .NET 10.0.13, BenchmarkDotNet v0.15.8, Windows 11 on an otherwise idle i9-12900HK; out of process, three launches, pinned to two performance cores. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published._

Ardalis.Specification is the most-used specification library in .NET — class-based, designed for EF Core query composition. ZA.Specification's struct-based design pays off across every operation that exercises both libraries' core surface.

| Operation | Ardalis.Specification time ÷ ZA's | Ardalis alloc | ZA alloc |
|---|---:|---:|---:|
| Construct composed spec | ZA below resolution ¹ | 792 B | **0 B** |
| Compose two specs | ZA below resolution ¹ | 1,752 B | **0 B** |
| Evaluate over 100 items (in-memory) | **630×** (632–633) | 4,743 B | **0 B** |

¹ ZA.Specification's construct and compose rows are below what BenchmarkDotNet can tell apart from an empty method: the JIT reduces building two empty structs to nothing, while Ardalis' rows are measurable, so no finite ratio can be given. Ardalis' compose takes 2.74 times as long as its construct. Earlier versions of this table charged ZA 24 B per composed spec; that was the benchmark boxing the struct into `object`, not the library.

The in-memory-evaluation gap is dramatic because Ardalis is designed for **IQueryable composition** — `WhereExpression.Filter.Compile()` runs on every call, almost all of the Ardalis row. For EF Core / database queries where the SQL provider dominates the cost, this overhead is invisible. For in-memory filtering (which many users do), it dominates.

ZA.Specification's `IsSatisfiedBy` is a direct, non-virtual call on a struct value — the JIT inlines it, the composition tree resolves to a sequence of `&&` operators in straight-line code. Zero allocation per evaluation regardless of composition depth.
<!-- SPECIFICATION:END -->

#### StateMachine
<!-- STATEMACHINE:START -->
_Imported from ZA.StateMachine — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21. Stateless 5.20.1, BenchmarkDotNet v0.15.8, .NET 10.0.13, Windows 11 on an otherwise idle i9-12900HK; out of process, three launches, pinned to two performance cores. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published._

[Stateless](https://github.com/dotnet-state-machine/stateless) is the de-facto state-machine library in .NET — class-based, fluent configuration, runtime trigger dispatch. ZA's source-generated switch dispatch dominates every comparable scenario.

| Operation | Stateless time ÷ ZA's | Launch range | Stateless alloc | ZA alloc |
|---|---:|---:|---:|---:|
| Fire valid (3-step cycle) | **205×** | 203–208 | 7,600 B | **0 B** |
| Fire invalid | **13.8×** | 13.5–14.0 | 24 B | **0 B** |
| Guard allowed | ZA below resolution | | 4,336 B | **0 B** |
| Guard blocked | ZA below resolution | | 768 B | **0 B** |

ZA's rows allocate nothing: the benchmark puts the machine back in its initial state with the generated `Reset()`, which writes the state field in place. ZA's guard rows are below what BenchmarkDotNet can tell apart from an empty method while Stateless' are measurable, so no finite ratio can be given; Stateless' allowed guard takes 3.9 times as long as its blocked one. The Stateless row's 7,600 B is two things combined: each `Fire` walks a `Dictionary<TTrigger, StateRepresentation>` and constructs trigger/transition info objects, **and** the per-iteration `BuildStatelessOrder()` reset rebuilds the configuration (`new StateMachine<,>(initial)` plus three `Configure().Permit()` calls — each one allocating dictionary entries).

This is the apples-to-apples comparison for cyclic state machines — a per-request handler in a web app, or a fresh circuit-breaker per stream. Both libraries pay a "reset" cost in this workload. ZA's reset is a single field write because configuration is resolved at compile time; Stateless's includes the full fluent-configuration rebuild because configuration is runtime. The dispatch portion alone (excluding the rebuild) is roughly an order of magnitude smaller on the Stateless side but still measurably slower than ZA's `switch`-expression `TryFire`.
<!-- STATEMACHINE:END -->

#### Resilience
<!-- RESILIENCE:START -->
_Imported from ZA.Resilience — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21. Polly.Core 8.8.0, BenchmarkDotNet 0.15.8, .NET 10.0.13 on Windows 11, i9-12900HK, on an otherwise idle machine. Every benchmark ran out of process in three launches, pinned to two performance cores. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published._

[Polly](https://github.com/App-vNext/Polly) v8 (`ResiliencePipeline`) is the de-facto resilience library in .NET. Every row was measured side by side in one BenchmarkDotNet run. "ZA vs Polly" divides the Polly row's mean by the ZA row's for the same operation; the brackets give the lowest and highest of the three launches.

| Operation | ZA vs Polly | Launch range | Polly alloc | ZA alloc |
|---|---:|---:|---:|---:|
| Retry, happy path | **20.0× faster** | 18.8–21.0 | 64 B | **0 B** |
| CircuitBreaker, closed | **23.3× faster** | 21.6–24.7 | 64 B | **0 B** |
| Retry with 2/3 failures | parity, 1.00× | 0.99–1.01 | 3,108 B ¶ | **952 B** ¶ |
| Retry with 2/3 failures, **backoff=0** (isolates loop overhead) | **2.8× faster** | 2.78–2.80 | 1,984 B | **576 B** |
| All-policies stacked, happy path | **7.7× faster** | 6.95–8.21 | 104 B | 144 B |
| All-policies stacked, retry triggers (2/3 fail) | parity, 1.01× † | 1.01–1.02 | 3,322 B ¶ | **1,320 B** ¶ |
| All-policies stacked, CB open (fast-reject) | **1.30× faster** | 1.26–1.33 | 1,352 B | **912 B** |

† These two rows need a per-iteration reset, so BenchmarkDotNet ran them as a separate job with one invocation per iteration; the comparison divides their means from the same run.

¶ These four rows wait on `Task.Delay`, so their timers and continuations allocate on other threads, and BenchmarkDotNet's figure moved between runs: the previous capture reported 7,904 B and 6,008 B for the all-policies pair, and this run 4,464 B for Polly's. They were re-measured with the precise process-wide counter, `GC.GetTotalAllocatedBytes(true)`, over 100 calls each after warm-up, and the table shows those figures.

The happy-path gap is driven by Polly's `ResiliencePipeline.ExecuteAsync` walking the strategy chain via delegate dispatch and allocating a `ResilienceContext` per call (64 B). ZA emits one direct method per interface — the retry/CB checks are inline `if` statements and `Volatile.Read` calls. No context object, no closure, no delegate.

**Retry with failures.** With a 1 ms backoff both libraries spend almost all of each call in two `Task.Delay(1)` waits, which on Windows each last a timer tick of about 15.6 ms, so they are level and the timer, not the retry loop, sets the figure. At backoff 0 the loop itself is measured, and ZA is 2.8× faster with 71% less allocation.

**All-policies stacked comparison.** Three rows compare a 4-policy stack (Retry + Timeout + RateLimit + CircuitBreaker). **Happy path** measures cumulative dispatch overhead — ZA wins because the generator emits one flat method with inline policy checks, while Polly walks the strategy chain with delegate dispatch. ZA allocates 40 B more: the linked `CancellationTokenSource` and timer that `[Timeout]` creates, 144 B. **Retry triggers** measures the most realistic prod failure mode — inner fails 2/3, retry recovers; both libraries are bound by the same `Task.Delay(1ms)` floor, and ZA allocates 60% less.

**CB open (fast-reject)** measures the steady-state cost when the circuit is open. The generated proxy checks `CanExecute()` once, before it creates the timeout's `CancellationTokenSource` or enters the retry loop, and throws `ResilienceException` straight away, so an open circuit is never retried; see [Execution Order](core-concepts/execution-order.md). Polly's retry is configured not to handle `BrokenCircuitException`, so it too rejects once per call. The 912 B ZA allocates is the cost of throwing that one `ResilienceException` and catching it through the awaited `ValueTask`: the single-policy CircuitBreaker-open and RateLimit-exhausted rows in the self-benchmark below have no retry at all and allocate the same 912 B. Polly's rejection costs 1,352 B.

Rate-limit and timeout limits in the all-policies harness are set to `int.MaxValue` permits / 60s ResetMs so neither policy trips during measurement. The rate-limiter apples-to-apples comparison is deferred because the two libraries' rate-limiter implementations differ (Polly wraps `System.Threading.RateLimiting.ConcurrencyLimiter`; ZA has its own throughput-based impl).
<!-- RESILIENCE:END -->

#### Rest
<!-- REST:START -->
_Imported from ZA.Rest — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

Each row's time relative to the raw `HttpClient` call of the same shape, and Refit's relative to ZeroAlloc.Rest's.

| Call | Raw `HttpClient` | ZeroAlloc.Rest | Refit | ZeroAlloc.Rest vs Refit | Allocated: raw / ZA / Refit |
|---|---:|---:|---:|---:|---:|
| GET | 1.00 | 1.24 | 1.86 | **1.49× faster** (1.47–1.51) | 1,408 / **1,584** / 2,056 B |
| POST | 1.00 | 1.27 | 1.63 | **1.29× faster** (1.26–1.31) | 1,744 / **2,088** / 2,816 B |
| GET with query parameter | 1.00 | 1.22 | 2.39 | **1.96× faster** (1.57–2.30) | 1,480 / **1,616** / 2,128 B |
| DELETE | 1.00 | 1.21 | 1.32 | **1.09× faster** (0.99–1.17) | 1,136 / **1,312** / 1,416 B |
| GET returning `Result<T, HttpError>` | 1.00 | 1.24 | — | Refit lacks `Result<T>` | 1,352 / **1,632** / — |

ZeroAlloc.Rest is **1.1–2.0× faster than Refit** across every shape of call (GET / POST / GET-with-query / DELETE) and allocates **1.1–1.35× less**. It runs at 1.21–1.27× the raw `HttpClient` baseline on every shape. Refit, at 1.3–2.4× over raw, has narrowed the gap since the May capture, which measured it against ZeroAlloc.Rest at 1.7–3.6×; Refit has been upgraded three times since then, to 16.2, 16.3 and 16.4.
<!-- REST:END -->

#### Serialisation
<!-- SERIALISATION:START -->
_Imported from ZA.Serialisation — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

### Deserialize

| Library | Wrapper time | Raw allocation | Wrapper allocation | Extra allocation |
|---|---:|---:|---:|---:|
| MemoryPack | 2.23× (2.11–2.40) | 64 B | 64 B | **0 B** |
| MessagePack | 1.27× (1.25–1.29) | 64 B | 64 B | **0 B** |
| System.Text.Json | 0.99× (0.97–1.02) | 64 B | 64 B | **0 B** |

The 64 B is the deserialized object itself. MessagePack has no span overload, so the wrapper copies the span into a pooled array before handing it over; the copy allocates nothing. See [the MessagePack backend](backends/messagepack.md#deserializing-from-a-span).

### Serialize

| Library | Wrapper time | Raw allocation | Wrapper allocation | Extra allocation |
|---|---:|---:|---:|---:|
| MemoryPack | 1.21× (1.04–1.27) | 0 B | 0 B | **0 B** |
| MessagePack | 1.25× (1.24–1.27) | 0 B | 0 B | **0 B** |
| System.Text.Json | 1.23× (1.22–1.23) | 0 B | 0 B | **0 B** |

The wrapper's cost is a small fixed amount per call, so it shows most against the fastest library: MemoryPack deserializes this payload in so little time that the wrapper more than doubles it, while against System.Text.Json it is within the launch spread.

The raw System.Text.Json row reuses one `Utf8JsonWriter`, as a caller writing to an `IBufferWriter<byte>` would. The wrapper does the same with one writer per thread; until #198 it created a writer per call, 136 B.

### Generated serializers and dispatcher

`GeneratedSerializerBenchmarks` measures the serializers the source generator emits, and the generated `SerializerDispatcher`:

| Path | Allocated |
|---|---:|
| Generated Serialize, all three formats | 0 B |
| Generated Deserialize, all three formats | 64 B, the result |
| Dispatcher `Deserialize(data, type)`, all three formats | 64 B, the result |
| Dispatcher `Deserialize<T>(data)`, struct with no reference members, MemoryPack and MessagePack | 0 B |
| Dispatcher `Deserialize<T>(data)`, struct with no reference members, System.Text.Json | 32 B, the box `JsonSerializer` builds the struct in |
| Dispatcher `Serialize(object, Type)` | 312 B, the `ArrayBufferWriter<byte>` and array it returns, and the box for a struct |
| Dispatcher `Serialize(writer, value, type)` into a reused writer | 0 B |
| Dispatcher `Serialize<T>(writer, value)` into a reused writer, class or struct | 0 B |

The dispatcher's `Serialize(object, Type)` returns memory the caller owns, so that buffer is its contract. The `IBufferWriter<byte>` overloads write into the caller's writer instead, and the generic one does not box a struct; `DispatcherBufferWriterTests` asserts 0 B for the generic overload with a struct in each of the three formats and with a class, and for the by-type overload. A caller that needs an exact-size `byte[]` pays for that array only: one `ToArray()` of the written span, against 376 to 416 B for `Serialize(object, Type)` followed by `ToArray()`.

`Deserialize(data, type)` returns `object`, so a struct is boxed on top of what the format allocates. `Deserialize<T>(data)` returns the struct as `T`: `DispatcherGenericDeserializeTests` asserts that it allocates exactly what the per-type serializer allocates, and one 32 B box less than the `object` overload for a 16-byte struct, in each of the three formats. A positional record struct read with System.Text.Json costs 168 B inside `JsonSerializer`, for its constructor arguments, whichever overload reads it.
<!-- SERIALISATION:END -->

#### Cache
<!-- CACHE:START -->
_Imported from ZA.Cache — last refreshed 2026-10-22._

_Last refreshed: 2026-10-17_

L1 (in-process) cache-hit comparison. ZA.Cache wraps `IMemoryCache`, so the relevant comparisons are: hand-rolled `GetOrCreateAsync` (the pattern ZA replaces) and [FusionCache](https://github.com/ZiggyCreatures/FusionCache) 2.9 (the de-facto third-party L1+L2 caching library). Each row returns the library's own task type directly, so no benchmark wrapper allocation is counted.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Every benchmark ran out of process in three launches on an otherwise idle machine, pinned to two performance cores (`--affinity 20`). `Ratio` is each row's mean time relative to the baseline in the same run; `Launch range` is the lowest and highest ratio of the three launches.

| Library | Ratio | Launch range | Allocated |
|---|---:|---:|---:|
| Raw `IMemoryCache.GetOrCreateAsync` | 1.00 | | 104 B |
| **ZA.Cache proxy** | **1.66** | **1.61–1.68** | **0 B** |
| FusionCache | 6.88 | 6.79–7.04 | 88 B |

**ZA.Cache is about 4× faster than FusionCache and the only row that allocates nothing.** The trade vs raw `IMemoryCache` is about 1.7× the time, the cost of the typed `[Cache]` abstraction: proxy dispatch, key formatting and telemetry. In exchange you don't write the cache-lookup boilerplate at every call site, and the key derivation is generated rather than hand-typed.

**FusionCache** is heavier because it carries L2-cache, stampede protection, and adaptive-caching infrastructure even when only L1 is configured. For pure L1, ZA is the lighter choice; FusionCache's value is the L2 + advanced features that ZA does not implement.

**Caveat on the raw row**: its 104 B is the `Task<string>` that `GetOrCreateAsync` returns plus the boxed `(string, int)` tuple the test uses as its key. ZA's hit returns a synchronously completed `ValueTask<string>` and looks the key up as a span, so it allocates nothing.
<!-- CACHE:END -->

#### Telemetry
<!-- TELEMETRY:START -->
_Imported from ZA.Telemetry — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

The realistic alternative to ZA.Telemetry's generator is hand-written `ActivitySource` + `Meter` wrapping — wrapping every instrumented method with `using var activity = source.StartActivity(...); counter.Add(1); histogram.Record(...)`. This benchmark compares the two in the no-listeners profile (the common production case after sampling).

.NET 10.0.13, BenchmarkDotNet v0.15.8, Windows 11 on an otherwise idle i9-12900HK; out of process, three launches, pinned to two performance cores. The benchmark methods return the service's `ValueTask<int>` directly, so the rows show only what each wrapper adds.

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

| Method | Time vs ZA proxy | Launch range | Allocated |
|---|---:|---:|---:|
| Hand-written `ActivitySource` + `Counter` + `Histogram` | **7.51×** | 7.47–7.59 | 0 B |
| **ZA.Telemetry generated proxy** | **1.00** | | **0 B** |

Neither wrapper allocates in the no-listeners path. Against the uninstrumented direct call, which takes well under a nanosecond, the proxy takes 8.1 times as long and the hand-written wrapper 61 times. The difference is the short-circuit described above: with no listener on the source and no instrument enabled, the proxy returns the inner `ValueTask` as is, while the hand-written version still runs its `async` state machine, takes two `Stopwatch` timestamps, and calls `Counter.Add` and `Histogram.Record` on instruments nobody is listening to. Hand-written code can do the same, but every method has to remember to.

**The takeaway**: ZA.Telemetry's value isn't faster instrumentation than hand-writing it — it's eliminating the boilerplate so every instrumented method gets the same try/finally + counter + histogram pattern, with zero risk of forgetting to dispose an Activity or skipping a metric.
<!-- TELEMETRY:END -->

#### Notify
<!-- NOTIFY:START -->
_Imported from ZA.Notify — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Measured on an otherwise idle machine, on .NET 10 and .NET 8, out of process, in three launches pinned to two performance cores. Ratio is each row's mean time relative to the hand-written `INotifyPropertyChanged` in the same run, with the lowest and highest of the three launches in brackets. All four libraries have five handlers attached, to mirror a realistic MVVM scenario. CommunityToolkit.Mvvm 8.4.2, PropertyChanged.Fody 4.1.0.

| Library | Ratio, .NET 10 | Ratio, .NET 8 | Allocated | Async support |
|---|---:|---:|---:|:---:|
| Manual `INotifyPropertyChanged` (baseline) | 1.00 | 1.00 | 24 B | ❌ |
| PropertyChanged.Fody | **0.93** (0.91–0.95) | **1.02** (1.02–1.03) | **0 B** | ❌ |
| CommunityToolkit.Mvvm | 1.81 (1.74–1.91) | 1.84 (1.80–1.86) | 0 B | ❌ |
| **ZeroAlloc.Notify** | **1.47** (1.42–1.54) | **1.63** (1.62–1.66) | **40 B** | ✅ |

**Honest framing**: ZA.Notify is slower than a hand-written `INotifyPropertyChanged` and Fody, and it allocates. It is faster than CommunityToolkit.Mvvm here, which allocates nothing. The 40 B is the `AsyncPropertyChangedEventArgs` object handed to the handlers, much like the `PropertyChangedEventArgs` the manual INPC baseline allocates. The view model also declares `PropertyChanging`, which has no subscribers here, so its args are never created. The other three are pure sync and cannot `await` propagation.

**ZA.Notify is the only library here that supports async handlers.** The trade-off is the cost of that capability. For pure-sync view models that never need to `await` a handler (the vast majority of XAML/Avalonia/WinUI scenarios), **Fody is the right choice**: it is as fast as hand-written code and allocates nothing. For scenarios where the setter needs to wait for async work to complete before returning (e.g., async validation before notifying observers; coordinated async state transitions), ZA.Notify is the only library that supports it.

The 2026-10-11 capture put ZA.Notify at 3.2 times the hand-written baseline. This run, three pinned launches on an idle machine, replaces it.

### Where the allocations come from

`NotifyAllocationBenchmarks`, same runs. The view model declares both `PropertyChanging` and `PropertyChanged`; "five subscribers" means five `PropertyChanged` handlers with the default parallel dispatch. Ratio is relative to the `string` setter with no subscribers.

| Setter | No subscribers | Five subscribers | Ratio, .NET 10 | Ratio, .NET 8 |
|---|---:|---:|---|---|
| `string` property | 0 B | 40 B | 1.00 → 2.06 (1.96–2.14) | 1.00 → 2.06 (2.01–2.09) |
| `int` property | 0 B | 88 B | 0.47 (0.45–0.48) → 2.12 (1.92–2.45) | 0.86 (0.81–0.90) → 2.23 (2.15–2.30) |

The `int` property with subscribers pays the 40 B args plus a 24 B box each for the old and the new value.
<!-- NOTIFY:END -->

#### Scheduling
<!-- SCHEDULING:START -->
_Imported from ZA.Scheduling — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published._

Coravel 6.0.2 (the de-facto in-process scheduler in .NET) and a hand-rolled `BackgroundService + Timer` baseline (the pattern most apps reach for before adopting a library). BenchmarkDotNet 0.15.8, .NET 10.0.13, Windows 11 on an otherwise idle i9-12900HK, three launches per benchmark, out of process, pinned to two performance cores with `--affinity 20`.

### vs Coravel: registration cost

| Operation | Coravel allocation | ZA.Scheduling | Coravel time, same run |
|---|---:|---:|---:|
| Single `Schedule()` / `[Job]` registration | 696 B per call | **compile-time, 0 B at runtime** | baseline |
| 100 schedule calls (queue accumulation) | 77,040 B, 110.69× one call | **compile-time, 0 B at runtime** | 11.39× one call |

**Honest reading**: Coravel's `Schedule()` is a runtime call that allocates an entry the scheduler walks every tick. ZA.Scheduling's `[Job]` registration happens at compile time — the source generator emits one direct dispatcher per attributed type. There is no equivalent runtime registration call in ZA, so the comparison is between Coravel's per-call registration cost and ZA's no-cost-at-all (the cost moved to build time). The 696 B per `Schedule()` is the realistic cost in a Coravel app that schedules jobs dynamically at startup.

The 100-call ratio is the least stable figure here: the three launches gave 9.4 to 12.9, and an earlier capture on a shared VM reported 15.36. Both Coravel rows rebuild the scheduler before every iteration, so they run one invocation per iteration and BenchmarkDotNet warns that the iterations are short.

### vs naive `BackgroundService + Timer` baseline: dispatch overhead

| Operation | Naive direct call | ZA.Scheduling `IJob.ExecuteAsync` |
|---|---:|---:|
| Single dispatch | indistinguishable from an empty method, 0 B | indistinguishable from an empty method, 0 B |
| 100 dispatches | — | 0 B |

BenchmarkDotNet flags both single-dispatch rows as "indistinguishable from empty-method duration", in all three launches, so it computes no ratio between them. **ZA.Scheduling adds no measurable overhead over a direct method call** — the `[Job]`-attributed proxy compiles to a direct return for synchronous completions, and the JIT inlines both bodies entirely.

The takeaway: if you're considering ZA.Scheduling over a hand-rolled `Timer`, the dispatch cost is identical. The value of the abstraction is the `[Job]` attribute itself (declarative retries, cron, store-backed persistence) — not raw dispatch speed.

Reproduce from `benchmarks/ZeroAlloc.Scheduling.Benchmarks`:

```bash
dotnet run -c Release -- --filter '*CoravelComparisonBenchmark*' '*NaiveTimerOverheadBenchmark*' --launchCount 3 --affinity 20
```
<!-- SCHEDULING:END -->

#### Outbox
<!-- OUTBOX:START -->
_Imported from ZA.Outbox — last refreshed 2026-10-22._

_Last refreshed: 2026-10-21_

Correctness-matched comparison: both rows use the **same SQLite-in-memory connection** (different tables), both wrap each enqueue in a transaction, both run a claim-then-dispatch-then-commit cycle on the dispatch tick. Storage variance cancels out — the remaining delta is the cost of the `IOutboxWriter<T>` + `IOutboxStore` + serializer abstraction layer.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Each benchmark ran out of process in three launches on an otherwise idle machine, pinned to two performance cores. `Ratio` is ZA.Outbox's mean time relative to the hand-rolled row of the same operation in the same run, with the lowest and highest of the three launches in brackets.

| Operation | Hand-rolled | ZA.Outbox | Allocated, hand-rolled → ZA.Outbox |
|---|---:|---:|---:|
| Enqueue (1 message, transactional) | 1.00 | **1.03** (1.02–1.04) | 2,128 B → 2,184 B, +3% |
| Dispatch tick (10 messages) | 1.00 | **1.05** (1.00–1.12) | 12,184 B → **11,520 B, −5%** |

**Honest reading**: ZA.Outbox adds **near-zero abstraction overhead** vs writing the same correctness-matched SQLite outbox by hand. The 3–5% time delta is the `IOutboxWriter<T>` + `IOutboxStore` interface dispatch + serializer call overhead. Memory allocation is within ±5% of hand-rolled, and lower on the dispatch tick.

The value of the abstraction is the `[OutboxMessage]` attribute + the typed writer + composability with other ZA packages (dispatcher, dashboard, resilience, telemetry bridges) — paid for at near-no runtime cost.
<!-- OUTBOX:END -->

#### EventSourcing
<!-- EVENTSOURCING:START -->
_Imported from ZA.EventSourcing — last refreshed 2026-10-22._

_Last refreshed: 2026-10-17_

Correctness-matched comparison: both rows use the **same SQLite-in-memory connection** (different tables), both check the current stream version inside a transaction before INSERT, both run an ordered SELECT across the stream on read. Storage variance cancels out — the remaining delta is the cost of the `IEventStore` + `IEventStoreAdapter` + `IEventSerializer` + `IEventTypeRegistry` abstraction layer.

To isolate the abstraction overhead from real-world serialization cost (which both sides would pay equally in production), the ZA row uses a cached no-op serializer. The hand-rolled row similarly stores raw bytes without round-tripping JSON.

Each ZA row's ratio is against the hand-rolled row of the same operation in the same run.

| Operation | Framework | Hand-rolled | ZA.EventSourcing | Launch range | Allocated, hand-rolled → ZA |
|---|---|---:|---:|---:|---:|
| Append 1 event (transactional, OCC check) | net10.0 | 1.00 | **1.20** | 1.06–1.35 | 3,888 B → 4,688 B |
| | net8.0 | 1.00 | **1.30** | 1.26–1.37 | 3,888 B → 4,680 B |
| Read 100-event stream (ordered) | net10.0 | 1.00 | **2.03** | 1.99–2.04 | 12,232 B → 25,632 B |
| | net8.0 | 1.00 | **2.10** | 2.07–2.13 | 12,232 B → 25,632 B |

**Honest reading**: ZA.EventSourcing adds **a measurable abstraction tax** over a raw SQLite event store — 20–30% time on append and about 2× on read. That tax buys you:

- Typed events through `IEventStore.AppendAsync<TEvent>` / `ReadAsync` (the hand-rolled version stores raw bytes the caller has to ser/deser themselves)
- Pluggable serialization through `IEventSerializer` (so you can swap System.Text.Json for MessagePack, protobuf, or your own zero-alloc serializer without touching the store)
- Optimistic concurrency through a typed `StreamPosition` API
- Composability with the rest of the ecosystem (`Aggregate<T>`, projections, snapshots, upcasters, dead-letter handling)

In production the dominant cost on both sides is the serializer + the SQL round-trip — at the millisecond scale of a real database both rows converge and this delta becomes invisible.
<!-- EVENTSOURCING:END -->

### Reproduction

```bash
# Allocation per request (BDN, in-process)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# RPS under load (NBomber, real Kestrel). Stub the shipping client so the
# seed step doesn't DNS-fail against the placeholder shipping URL.
Shipping__UseStub=true dotnet run --project src/MyApp.Api &
dotnet run -c Release --project benchmarks/MyApp.LoadTest
```

### NativeAOT status

AOT publish works end-to-end post-swap. CI's `aot-publish-smoke` job runs `dotnet publish -p:PublishAot=true -r linux-x64` on every push and asserts the produced binary boots and answers `/healthz`. The previous EF Core-anchored gaps (no `MigrateAsync`, no LINQ-to-SQL under `--precompile-queries`, no `ComplexProperty` on `readonly struct` value-objects) are all gone because the persistence layer no longer routes through EF: ZA.ORM emits hand-shaped ADO.NET commands at compile time, `MigrationRunner` reads embedded SQL resources at startup with zero reflection, and value objects round-trip through the static `MoneyConverter` helper into TEXT columns. Migrations are hand-authored SQL under `src/MyApp.Infrastructure/Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` — picked up by version-prefix on next startup, no regen step. The reference shape for both the write and the raw-SQL-flavoured read path is [`OrderRepository.cs`](../content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs).

## Customising

Three extensions you'll likely make first.

### Swap SQLite → PostgreSQL

Both providers are already wired in `InfrastructureServiceCollectionExtensions.AddMyAppInfrastructure(provider, connectionString, ...)`. The `provider` argument is a string: `"Postgres"` (case-insensitive) registers a scoped `NpgsqlConnection` adapted to `IAsyncDbConnection`; any other value falls through to `SqliteConnection`. Flip the value in `Program.cs` (or via `Database:Provider` configuration if you've moved it to `appsettings.json`) and update `ConnectionStrings:Default` to a Postgres connection string.

The matching migration set already exists at `src/MyApp.Infrastructure/Persistence/Migrations/Postgres/`. The `MigrationRunner` consults the `__zaorm_migrations` history table on startup and applies any unseen `NNN_<name>.sql` files in order. No EF tooling step required. To add a Postgres-only schema change, drop a new `NNN_<name>.sql` into that directory; SQLite-specific syntax in any custom migration (e.g. `AUTOINCREMENT` vs `GENERATED ... AS IDENTITY`) needs to live in the per-provider folder it applies to.

A deeper reference for ZA.ORM-style migrations lives at [orm.zeroalloc.net/cookbook/migrations](https://orm.zeroalloc.net/cookbook/migrations).

### Add a New Endpoint

The slice pattern is consistent. For a hypothetical `CancelOrder`:

```
src/MyApp.Application/CancelOrder/
  CancelOrderCommand.cs          // record + [Validate]
  CancelOrderHandler.cs          // [Scoped] IRequestHandler<,>
  CancelOrderValidator.cs        // optional explicit validator
src/MyApp.Api/
  Endpoints/OrdersEndpoints.cs   // app.MapPost("/orders/{id}/cancel", ...)
  Dtos/CancelOrderRequest.cs
  Mappings/OrderMappings.cs      // add [Map<CancelOrderRequest, CancelOrderCommand>]
```

ZA.Inject's source generator picks up the `[Scoped]` handler on the next build and adds it to `AddMyAppApplication()` automatically — no manual registration.

### Change Auth

The DEV JWT signing key is `Jwt:DevSigningKey` in `appsettings.json`. **Remove it before any non-dev deployment.** For production, configure full validation against your real identity provider:

```csharp
.AddJwtBearer(opt =>
{
    opt.Authority = builder.Configuration["Jwt:Authority"];  // your issuer
    opt.Audience  = builder.Configuration["Jwt:Audience"];
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        // signing keys resolved from the OIDC discovery doc
    };
});
```

The `OrdersRead` / `OrdersWrite` policies stay as-is — only the issuance and validation layer changes. Endpoints already call `.RequireAuthorization("OrdersWrite")` and don't need to know where the token came from.

## Where to Next

- **Per-package depth** — every package in the table above links to its own docs site.
- **The plan** — the template's design rationale lives in `docs/za-clean-template.md` at the repo root.
- **Issues and contributions** — [github.com/ZeroAlloc-Net/ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).

## Load testing against Postgres

NBomber's `MyApp.LoadTest` previously targeted in-memory SQLite via the production app — capped at ~470 RPS by SQLite's single-process file lock. That ceiling is the lock, not the framework. Running against Postgres reveals the real throughput.

The SUT and NBomber run as separate processes. The SUT is configured for Postgres via env vars; NBomber's scenario code is unchanged. AOT-correct: the production startup applies the embedded `Persistence/Migrations/Postgres/*.sql` files via `ZeroAlloc.ORM.Migrations.MigrationRunner`, which reads the embedded resources and tracks state in `__zaorm_migrations` — pure ADO.NET, zero reflection at runtime.

### Local recipe

```bash
# 1. Start Postgres
docker run --rm -d -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
  --name myapp-load-pg postgres:17 \
  -c max_connections=500

# 2. Start the SUT
Database__Provider=Postgres \
Database__SchemaStrategy=EmbeddedScript \
ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
dotnet run -c Release --project src/MyApp.Api &

# 3. Wait for /healthz, then run NBomber
until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
dotnet run -c Release --project benchmarks/MyApp.LoadTest

# Cleanup
kill %1; docker stop myapp-load-pg
```

### CI

The `nbomber-postgres-clean` job in `.github/workflows/benchmarks.yml` runs the recipe above end-to-end on every manual workflow trigger. Artifacts:

- `nbomber-za-clean-postgres` — NBomber's HTML / CSV / Markdown reports.
- `nbomber-sut-log-clean` — the SUT's stdout/stderr (kept short, 7-day retention).

### Numbers — `Benchmarks (manual)` workflow run [26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747) (post-swap)

NBomber open-model `Inject(rate=5000/s)`, 30 s steady-state + 10 s ramp, scenario `read_order_by_id` (`GET /orders/{id}` against a seeded set of 1000 orders):

| Metric | Value |
|---|---:|
| OK / fail | **172,500 / 0** |
| **RPS (ok)** | **4,312.5** |
| Latency min / mean / max | 0.55 / 247.25 / 2,297.28 ms |
| Latency p50 / p75 / p95 / p99 | 188.8 / 318.0 / 678.9 / 1,136.6 ms |

The SUT, NBomber, and Postgres all run on the same GHA Linux runner (AMD EPYC 7763, .NET SDK 10.0.300). `Maximum Pool Size=500` matches the Postgres `max_connections=500`.

### Reading the numbers

**vs. the pre-swap EF-era Postgres run (`run 26644626697`, kept below for context).** Same scenario, same hardware, same SUT shape — only the persistence layer swapped (EF Core 10 → ZA.ORM 1.1) and the load shape moved from closed-model 500-VU to open-model 5k-RPS inject. The headline: 2,809 RPS → 4,312 RPS (+53%), p50 154 ms → 189 ms (open-model removes closed-loop backpressure; mean request work is now bounded by the wire to Postgres rather than by Little's-law steady-state). Zero failures vs 0.57% timeouts — the ZA.ORM hot path holds open longer under inject pressure.

**vs. file-SQLite baseline.** The same scenario against file-backed SQLite was historically capped at ~473 RPS (the EF-era closed-model NBomber row earlier on this page) by SQLite's single-process file lock. Post-swap Postgres lifts the ceiling to **4,312 RPS — about 9×**. Postgres handles concurrent reads via MVCC; file-SQLite serializes them.

**vs. za-vertical-slice.** za-vertical-slice's equivalent post-swap NBomber-Postgres run lands at the same 4,312 RPS / 172,500 ok / 0 fail (open-model `Inject` saturates both templates at the configured rate). Tail shapes differ: vs's p50 is 37 ms vs clean's 189 ms, reflecting the lighter slice-resolution path; clean's tail is tighter at p99 (1,137 ms vs 1,319 ms). Both confirm: **the ZA framework cost itself is provider-independent**, and architectural differences (slice-direct vs layered) shift the per-request budget without changing the throughput ceiling at this rate.

**Pipeline cost as a fraction of total request time.** ZA pipeline (mediator + validation + authorization) is well under 100 μs end-to-end. At 189 ms p50 / 679 ms p95, the budget is dominated by Postgres wire + Kestrel queueing — not by the framework hot path. For capacity planning, anchor on the Postgres row; for regression-detection of framework changes, the Sqlite row in the BDN table above is the tighter signal.

### Numbers — `Benchmarks (manual)` workflow run [26644626697](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26644626697) (pre-swap, historical)

500 concurrent VUs (closed-model), 30 s, scenario `read_order_by_id` (`GET /orders/{id}` against a seeded set of 1000 orders), EF Core 10 persistence:

| Metric | Value |
|---|---:|
| Total requests | 84,748 |
| OK | 84,265 |
| Fail | 483 (0.57%, all `operation timeout`) |
| **RPS** | **2,809** |
| Latency p50 / p95 / p99 | 154 ms / 286 ms / 370 ms |
| Latency mean / max | ~190 ms / ~2 s |

Kept for diff-over-time context; the post-swap table above is the current baseline.

**The 0.57% timeout failure rate** is consistent with the za-vertical-slice run — bounded by single-Postgres contention at 500 concurrent VUs. Production tuning would dial in `Maximum Pool Size`, `shared_buffers`, and possibly a Postgres read replica for the GET path.
