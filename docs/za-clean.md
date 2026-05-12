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

The Api boots, applies its EF Core SQLite migrations, seeds a sample order in `Development`, and listens on the kestrel default. In another shell:

```bash
$ curl http://localhost:5000/healthz
{"status":"ok"}
```

The same process is emitting OpenTelemetry traces to the console — `GET /healthz` produces a single `Microsoft.AspNetCore` span, and any subsequent `POST /orders` will produce a nested trace covering the mediator handler, the EF Core command, and the outbound HTTP call to the shipping client.

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
└──────────┬───────────────────┘
           ▼
┌──────────────────────────────┐
│ MyApp.Infrastructure         │  EfOrderRepository (EF Core + SQLite)
│  Persistence/, External/     │  ShippingQuoteHttpClient (ZA.Rest proxy)
└──────────┬───────────────────┘
           ▼
┌──────────────────────────────┐
│ MyApp.Domain                 │  Order, OrderLine, Money, OrderStatus
│                              │  Smart constructors, no deps.
└──────────────────────────────┘
```

**Domain** holds `Order`, `OrderLine`, `OrderStatus`, and the `Money` / `Sku` value objects. It has zero references to anything outside itself — no EF, no ASP.NET, no ZA packages other than ZA.Results for `Result<T, DomainError>`. Invariants live in smart constructors.

**Application** holds the CQRS slice: `CreateOrderCommand`/`Handler`, `GetOrderByIdQuery`/`Handler`, the `IOrderRepository` and `IShippingQuoteClient` abstractions, and `ApplicationError`. Handlers implement `IRequestHandler<TRequest, TResponse>` from ZA.Mediator and return `ValueTask<Result<T, ApplicationError>>`. A hand-rolled `CreateOrderValidator` runs at the top of `CreateOrderHandler.Handle` and short-circuits to a `validation.failed` `ApplicationError` on the first invalid field — intentional: one error per response keeps the template's API simple, and ZA.Validation's batched form will land here when its generator nupkg ships.

**Infrastructure** holds `AppDbContext`, `EfOrderRepository`, EF Core migrations, and `ShippingQuoteHttpClient` — a `[ZeroAllocRestClient]` interface that ZA.Rest + ZA.Resilience compose into a typed HTTP client with retry/timeout policies. `InfrastructureServiceCollectionExtensions.AddMyAppInfrastructure(...)` wires it all up.

**Api** holds `Program.cs`, the minimal-API endpoint groups, DTOs, and DTO ↔ domain mappings (ZA.Mapping). It composes the other layers, registers JWT auth + the `OrdersRead`/`OrdersWrite` policies, and configures OpenTelemetry.

## Each ZA Package's Role

The template references ten ZeroAlloc packages. Generators ship as separate `*.Generator` nupkgs and are wired with `PrivateAssets=all` so they don't transit to downstream consumers — relevant when you add more ZA packages later.

| Package | Where it lives in the template | Notes |
| --- | --- | --- |
| [ZA.Results](https://results.zeroalloc.net) | Domain (`Result<T, DomainError>`), Application (`Result<T, ApplicationError>`) | Smart-constructor failures surface as structured `Result`. |
| [ZA.Mapping](https://mapping.zeroalloc.net) | `MyApp.Api/Mappings/` | `[Map<DTO, Domain>]` static partial classes. Zero-alloc happy path. |
| [ZA.Validation](https://validation.zeroalloc.net) | `MyApp.Application/CreateOrder/CreateOrderValidator.cs` | ZA.Validation generator currently doesn't ship in the published nupkg, so the template ships a hand-rolled validator at [`src/MyApp.Application/CreateOrder/CreateOrderValidator.cs`](../content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderValidator.cs) which `CreateOrderHandler` invokes at the top of `Handle`. When the generator nupkg fix lands, replace the hand-rolled validator with `[Validate]` attributes on the command/query records. |
| [ZA.Mediator](https://mediator.zeroalloc.net) | All handlers in `MyApp.Application/*` | `IRequest<TResponse>` / `IRequestHandler<TRequest, TResponse>`. Handlers return `ValueTask<T>`. ActivitySource `ZeroAlloc.Mediator` is wired into OTel. |
| [ZA.Inject](https://inject.zeroalloc.net) | `EfOrderRepository`, `ShippingQuoteHttpClient`, all handlers | `[Scoped]` / `[Singleton]` / `[Transient]` attributes — **not** `[Service(ServiceLifetime.X)]`. Generated `AddMyAppApplication()` / `AddMyAppInfrastructure(...)` extensions compose registration. |
| [ZA.Authorization](https://authorization.zeroalloc.net) | (not used directly) | Host-agnostic. The template uses vanilla `AddAuthorizationBuilder` with `OrdersRead` / `OrdersWrite` policies — swap to ZA.Authorization when you need policy composition. |
| [ZA.Telemetry](https://telemetry.zeroalloc.net) | (available, not used directly) | `[Instrument]` / `[Trace]` source generator. The template uses vanilla OpenTelemetry; attribute-driven tracing is an add-on you opt into per method. |
| [ZA.Rest](https://rest.zeroalloc.net) | `MyApp.Infrastructure/External/IShippingQuoteClient.cs` | `[ZeroAllocRestClient]` on the interface. Generates `services.AddIShippingQuoteHttpClient(opts => opts.BaseAddress = ...)`. |
| [ZA.Resilience](https://resilience.zeroalloc.net) | Same interface, alongside Rest | `[Retry]` / `[Timeout]` attributes on the interface. Generates a proxy type that wraps the inner client. |
| [ZA.Rest.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest.Resilience) | Bridge package | Composes the Rest + Resilience generators so a single attribute-decorated interface yields both an HTTP client *and* a resilience proxy. |

## Boundary Tests

`tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` runs five NetArchTest rules against the four assemblies. All pass on a fresh scaffold:

1. `Domain_does_not_depend_on_anything_outside_Domain` — no EF, no ASP.NET, no Application/Infrastructure/Api.
2. `Application_does_not_depend_on_Infrastructure_or_Api` — and no EF / ASP.NET either.
3. `Infrastructure_does_not_depend_on_Api` — keeps composition one-directional.
4. `Handlers_live_in_Application_only` — `IRequestHandler<,>` implementations cannot leak into Domain/Infrastructure/Api.
5. `EF_DbContexts_live_in_Infrastructure_only` — `DbContext` subclasses cannot leak out.

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

**`MyApp.Benchmarks.Primitives` (BenchmarkDotNet, in-isolation)** — `PrimitivesBench` exercises each ZA layer standalone: mapping, mediator dispatch, validator, value-object construction, and an end-to-end chain. No ASP.NET, no EF, no HTTP. These are the numbers that deliver on the "zero-allocation through the framework hot path" claim. Expect ns/op and 0 B/op for the framework primitives themselves.

**`MyApp.Benchmarks` (BenchmarkDotNet, in-process)** — `WritePipelineBench` hosts the API via `WebApplicationFactory<Program>` and measures `POST /orders` end-to-end through middleware, model binding, mediator dispatch, validation, EF Core SaveChanges, and the outbound shipping call (stubbed). It reports allocation per request and median latency. In-process means you're measuring the *pipeline*, not the network — useful for spotting regressions, not capacity planning.

**`MyApp.LoadTest` (NBomber, real Kestrel)** — drives sustained concurrency against a real Kestrel process. Two terminals: one runs the Api, the other runs the load test. NBomber reports p50/p95/p99 latency and RPS. This is where you size your service.

### AOT publish

| | Value |
|---|---:|
| AOT binary size (win-x64, self-contained, single file) | 35.8 MB |
| AOT cold start (process → `/healthz` 200, best of 3) | ~1.0 s |
| JIT cold start (same scenario, best of 3) | ~2.2 s |
| AOT speedup | ~2.2× faster cold start |

Captured on .NET 10.0.7 / 2022 i9-12900HK / Windows 11. The template's `MyApp.Api.csproj` defaults to `<PublishAot>true</PublishAot>`. EF Core requires work-arounds for NativeAOT — see "Known limitations under NativeAOT" below for the specifics the template applies. JSON serialization uses `JsonContext` source-gen. `InvariantGlobalization=true` keeps the binary lean; adopters needing culture-sensitive parsing should set it to `false` and document the ICU dependency.

Reproduce:

```bash
dotnet publish src/MyApp.Api -c Release -r win-x64 -o ./aot-out
./aot-out/MyApp.Api  # serves on :5000
```

A caveat on the storage layer: the template ships SQLite-in-WAL because it's frictionless to scaffold. Read-heavy benchmarks are honest — WAL handles concurrent readers well. **Write-heavy benchmarks need PostgreSQL** for production-grade numbers; SQLite serialises writers and will under-report what your real stack can do. (The Mapperly LINQ-fallback comparison from ZA.Mapping's benchmark suite isn't relevant here — the template's BDN measures *its own* pipeline, not a vs-comparison.)

### Results

Measured on a 2022 i9-12900HK / Windows 11 / .NET 10.0.7. Single run; reproduce on your own hardware for capacity planning.

#### Primitives — ZA framework cost in isolation

Measured with `MyApp.Benchmarks.Primitives` — no ASP.NET, no EF, just the ZA packages. These are the numbers that deliver on the "zero-allocation through the framework hot path" claim.

| Method                    | Mean      | Error     | StdDev    | Allocated |
|-------------------------- |----------:|----------:|----------:|----------:|
| `Mapping_RequestToCommand`| 100.47 ns | 10.636 ns | 30.174 ns | 200 B (destination record + nested OrderItem[]) |
| `Mediator_DispatchOnly`   |  37.38 ns |  1.994 ns |  5.879 ns | 0 B       |
| `Validator_HandRolled`    |  40.35 ns |  3.332 ns |  9.824 ns | 0 B       |
| `ValueObject_TryCreate`   |  12.52 ns |  0.823 ns |  2.428 ns | 0 B       |
| `EndToEndPrimitives`      | 165.24 ns | 15.591 ns | 44.483 ns | 200 B (= mapping alone — chain adds 0 B) |

The decisive datapoint: `EndToEndPrimitives` matches `Mapping_RequestToCommand` byte-for-byte. The validator + mediator dispatch through the chain allocate **zero bytes**. The 200 B is the `CreateOrderCommand` record + nested `OrderItem[]` array — caller cost every framework pays, not ZA overhead.

Compare with the full-pipeline `WritePipeline` row below: that 156 KB is ASP.NET model binding + JSON + EF tracking, not ZA framework cost. Use the primitives table for "does the framework allocate", the pipeline table for "does the endpoint allocate".

#### Full pipeline (ASP.NET + EF Core in the mix)

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

| Method        | Mean     | Error     | StdDev    | Allocated |
|-------------- |---------:|----------:|----------:|----------:|
| WritePipeline | 2.037 ms | 0.1501 ms | 0.4427 ms | 156.75 KB |

The 156.75 KB is dominated by ASP.NET Core's request pipeline (model binding, JSON deserialization, response shaping) and EF Core's tracking buffer — not the ZA framework cost. The handler-level allocation (mapping + Mediator dispatch + Result construction) is in the low hundreds of bytes; the rest is HTTP plumbing every endpoint pays. Use this as a regression baseline, not a capacity-planning number.

#### NBomber — read-RPS scenario (real Kestrel)

`GET /orders/{id}` at 500 concurrent VUs for 30 seconds, after seeding 1000 orders. API run with `Shipping__UseStub=true` so the seed step bypasses the placeholder shipping endpoint (the config flag landed in v0.2.1 — see Customising below).

| Metric | Value |
|---|---:|
| OK / fail | **14,207 / 370** |
| RPS (ok) | **473.57** |
| Mean latency | 1009 ms |
| p50 / p75 / p95 / p99 | 887 / 1203 / 2138 / 2634 ms |
| Failure mode | operation timeout (370 / 14,577 = 2.5%) |

These reflect an unoptimised SQLite-backed scaffold under heavy concurrency. The 1 s mean / 2 s p95 latency is dominated by SQLite's single-file lock combined with EF Core's tracking-context allocation per request — fixable with PostgreSQL, `AsNoTracking()` on reads, response caching, and tighter connection pooling.

The scenario's purpose isn't to publish a benchmark; it's to give adopters a working harness pointed at a representative endpoint so the first thing they do on a new branch is run it and see how their changes moved the needle.

### Comparisons

Per-primitive comparisons against the ecosystem alternatives. These blocks are refreshed by `tools/import-comparisons.ps1`; raw markdown comes from each ZA package's own benchmark harness, ensuring per-package perf claims and the template's claims stay in sync.

#### Mapping
<!-- MAPPING:START -->
_Imported from ZA.Mapping — last refreshed 2026-05-12._

_Last refreshed: 2026-05-10_

### FlatIdentity

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 32.44 ns | 3.047 ns |  8.643 ns |  1.06 |    0.38 | 0.0014 |      72 B |        1.00 |
| ZeroAlloc_   | 30.79 ns | 2.696 ns |  7.288 ns |  1.01 |    0.33 | 0.0015 |      72 B |        1.00 |
| Mapperly_    | 26.29 ns | 2.500 ns |  7.132 ns |  0.86 |    0.31 | 0.0015 |      72 B |        1.00 |
| AutoMapper_  | 76.37 ns | 6.919 ns | 19.173 ns |  2.50 |    0.86 | 0.0014 |      72 B |        1.00 |


### FlatConversion

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 167.9 ns |  7.22 ns | 20.37 ns | 164.4 ns |  1.01 |    0.17 | 0.0010 |      48 B |        1.00 |
| ZeroAlloc_   | 185.8 ns | 10.58 ns | 30.36 ns | 175.4 ns |  1.12 |    0.22 | 0.0010 |      48 B |        1.00 |
| Mapperly_    | 201.9 ns | 11.18 ns | 31.71 ns | 197.7 ns |  1.22 |    0.24 | 0.0010 |      48 B |        1.00 |
| AutoMapper_  | 327.2 ns | 12.22 ns | 33.66 ns | 323.0 ns |  1.98 |    0.30 | 0.0010 |      48 B |        1.00 |


### Flattening

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 29.17 ns | 4.485 ns | 12.940 ns |  1.20 |    0.76 | 0.0015 |      72 B |        1.00 |
| ZeroAlloc_   | 22.88 ns | 2.493 ns |  7.031 ns |  0.94 |    0.50 | 0.0015 |      72 B |        1.00 |
| Mapperly_    | 22.95 ns | 2.385 ns |  6.727 ns |  0.94 |    0.49 | 0.0015 |      72 B |        1.00 |
| AutoMapper_  | 70.84 ns | 5.222 ns | 14.556 ns |  2.91 |    1.37 | 0.0014 |      72 B |        1.00 |


### Collection

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev    | Median   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| HandWritten_ | 39.57 μs | 4.549 μs | 13.051 μs | 37.90 μs |  1.11 |    0.51 | 1.5869 |      - |  78.18 KB |        1.00 |
| ZeroAlloc_   | 38.61 μs | 3.943 μs | 10.992 μs | 37.19 μs |  1.08 |    0.46 | 1.6479 | 0.1221 |  78.18 KB |        1.00 |
| Mapperly_    | 41.09 μs | 5.774 μs | 15.710 μs | 35.97 μs |  1.15 |    0.58 | 1.7090 | 0.1221 |  78.25 KB |        1.00 |
| AutoMapper_  | 30.44 μs | 3.239 μs |  9.294 μs | 28.52 μs |  0.85 |    0.38 | 1.8311 | 0.1221 |  86.52 KB |        1.11 |


### Polymorphic

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 11.25 ns | 0.997 ns | 2.778 ns |  1.06 |    0.36 | 0.0007 |      32 B |        1.00 |
| ZeroAlloc_   | 11.38 ns | 1.214 ns | 3.463 ns |  1.07 |    0.42 | 0.0007 |      32 B |        1.00 |
| Mapperly_    | 11.01 ns | 1.107 ns | 3.124 ns |  1.04 |    0.38 | 0.0007 |      32 B |        1.00 |
| AutoMapper_  | 48.76 ns | 2.724 ns | 7.726 ns |  4.59 |    1.30 | 0.0006 |      32 B |        1.00 |


### UpdateInPlace

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| HandWritten_ |  4.691 ns | 0.3778 ns | 1.0470 ns |  4.437 ns |  1.04 |    0.31 |         - |          NA |
| ZeroAlloc_   |  6.051 ns | 0.6478 ns | 1.8482 ns |  5.417 ns |  1.35 |    0.49 |         - |          NA |
| Mapperly_    |  6.000 ns | 0.3755 ns | 1.0468 ns |  5.838 ns |  1.33 |    0.35 |         - |          NA |
| AutoMapper_  | 83.935 ns | 0.6793 ns | 0.6022 ns | 83.985 ns | 18.66 |    3.59 |         - |          NA |


### TryMap

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3


```
| Method       | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| HandWritten_ | 222.6 ns | 13.84 ns | 39.71 ns | 208.5 ns |  1.03 |    0.25 | 0.0010 |      48 B |        1.00 |
| ZeroAlloc_   | 208.4 ns |  9.66 ns | 27.08 ns | 201.6 ns |  0.96 |    0.20 | 0.0010 |      48 B |        1.00 |
| Mapperly_    | 195.4 ns |  8.59 ns | 23.81 ns | 194.2 ns |  0.90 |    0.18 | 0.0010 |      48 B |        1.00 |
<!-- MAPPING:END -->

#### Mediator
<!-- MEDIATOR:START -->
_Imported from ZA.Mediator — last refreshed 2026-05-12._

| Method | ZeroAlloc.Mediator | MediatR | Ratio | ZA Alloc | MediatR Alloc |
|---|---:|---:|---:|---:|---:|
| Send | 0.5 ns | 78.3 ns | **~160×** | 0 B | 224 B |
| Publish (1 handler) | 6.1 ns | 243.8 ns | **~40×** | 0 B | 792 B |
| Publish (multi handler) | 6.6 ns | 332.4 ns | **~51×** | 0 B | 1032 B |
| Send + Pipeline | 2.8 ns | 101.8 ns | **~46×** | 0 B | 152 B |
| Send (static) | 0.7 ns | — | — | 0 B | — |
| Send (via IMediator DI) | 5.8 ns | 86.3 ns | **~15×** | 0 B | 224 B |
| Stream (5 items) | 202.8 ns | 654.4 ns | **~3×** | 104 B | 528 B |

ZeroAlloc.Mediator is **40–160× faster** than MediatR across all measured paths, with zero heap allocations on every non-streaming path.
<!-- MEDIATOR:END -->

#### Validation
<!-- VALIDATION:START -->
_**Status: blocked upstream.** Blocked on ZA.Validation generator-nupkg fix. [Validate] is currently decorative — the template ships a hand-rolled validator until the generator nupkg ships its analyzer. Tracking: https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues._
<!-- VALIDATION:END -->

#### Inject
<!-- INJECT:START -->
_Imported from ZA.Inject — last refreshed 2026-05-12._

_Last refreshed: 2026-05-12_

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 138 ns | 528 B |
| ZA.Inject Container — `BuildZeroAllocInjectServiceProvider()` | 10,998 ns | 11,192 B |
| ZA.Inject Standalone — `new …StandaloneServiceProvider()` | **4 ns** | **32 B** |
| Jab — `new JabContainer()` | 8 ns | 40 B |

## Resolution Benchmarks

| Scenario | MS DI | ZA Container | ZA Standalone | Jab | Allocated |
|---|---:|---:|---:|---:|---:|
| Transient (no deps) | 19.9 ns | **15.9 ns** | 19.6 ns | 20.6 ns | 24 B |
| Transient (1 dep) | 30.9 ns | 27.3 ns | **24.1 ns** | 47.5 ns | 48 B |
| Transient (1 property dep) | 43.7 ns | 26.9 ns | **21.9 ns** | N/A¹ | 48 B |
| Transient (2 deps) | **39.4 ns** | 58.3 ns | 53.1 ns | 101.1 ns | 104 B |
| Singleton | 6.3 ns | 6.9 ns | **5.4 ns** | 5.5 ns | 0 B |
| Decorated transient | 44.5 ns | **21.1 ns** | 22.3 ns | 28.8 ns² | 48 B |
| `IEnumerable<T>` (3 impls) | **67.8 ns** | 74.8 ns | 81.8 ns | 150.9 ns | 168 B |
| Open generic (closed type) | 13.5 ns | (delegates to MS DI) | **7.7 ns** | N/A³ | 24 B |
| Create scope | 60 ns / 128 B | 123 ns / 216 B | 56 ns / 88 B | **8 ns / 40 B** | — |
| Resolve scoped (full lifecycle) | 8,225 ns / 304 B | 4,808 ns / 120 B | 3,393 ns / 120 B | **3,025 ns / 120 B** | — |

_¹ Jab is constructor-only — no property injection._
_² Jab decorator wired via factory (no first-class decorator attribute)._
_³ Jab 0.10.x requires closed types at the `[ServiceProvider]` attribute level._

ZA.Inject is **competitive across every scenario** and the clear winner where the generator's domain knowledge matters most: property injection (2× MS DI), decorators (2.1× MS DI), open generics (1.8× MS DI). Jab leads on scope creation (its scope is the lightest of the four, by an order of magnitude), with ZA Standalone close behind on the full scoped-resolution lifecycle.
<!-- INJECT:END -->

### Reproduction

```bash
# Allocation per request (BDN, in-process)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# RPS under load (NBomber, real Kestrel). Stub the shipping client so the
# seed step doesn't DNS-fail against the placeholder shipping URL.
Shipping__UseStub=true dotnet run --project src/MyApp.Api &
dotnet run -c Release --project benchmarks/MyApp.LoadTest
```

### Known limitations under NativeAOT (as of EF Core 10.0.7)

The template publishes successfully under `<PublishAot>true</PublishAot>`, but EF Core's NativeAOT story is still maturing. We work around three gaps:

1. **No `MigrateAsync` / `EnsureCreatedAsync`.** Both require design-time model building (reflection-based). The template embeds the migration output as `schema.sql` and applies it on startup. Regenerate the script after any entity change:
   ```bash
   dotnet ef migrations script -i -o src/MyApp.Api/schema.sql --project src/MyApp.Infrastructure --startup-project src/MyApp.Api
   ```

2. **No LINQ-to-SQL for reads.** EF Core 10's compiled-model handles writes (`db.Orders.AddAsync`) but reads need `--precompile-queries`, which currently fails because Roslyn's AOT pass can't see source-generator output. The template's `OrderRepository.GetByIdAsync` uses raw SQL via `db.Database.GetDbConnection().CreateCommand()` and hand-materialises the aggregate through `Order.Materialize(...)`. Money columns round-trip through the shared `MoneyConverter` helper so the raw-SQL read path uses the same `"<amount>|<currency>"` parse rules as the EF `ValueConverter`. Pattern is shown in [`OrderRepository.cs`](../content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs) — clone the same shape for new read endpoints.

3. **No `ComplexProperty` on `readonly struct` value-objects.** EF Core 10's `--nativeaot` generator emits incorrect `[UnsafeAccessor(UnsafeAccessorKind.Field)]` with by-value `this`, fails runtime verification. The template routes value-object columns through `HasConversion(_moneyConverter)` instead — Money column is a single `TEXT` storing `"<amount>|<currency>"`.

When EF Core ships fixes upstream (precompile-queries source-gen visibility, ComplexProperty by-value-struct codegen), these workarounds become unnecessary. Until then: **raw SQL for reads, embedded schema script for migrations, ValueConverter for value-objects in entity roots**.

## Customising

Three extensions you'll likely make first.

### Swap SQLite → PostgreSQL

Replace the EF Core provider in `Program.cs` (or in `InfrastructureServiceCollectionExtensions` if you moved the registration there):

```csharp
// Before
options.UseSqlite(connectionString);

// After
options.UseNpgsql(connectionString);
```

Then:

```bash
dotnet add src/MyApp.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet ef migrations remove --project src/MyApp.Infrastructure
dotnet ef migrations add InitialPostgres --project src/MyApp.Infrastructure
```

Update `ConnectionStrings:Default` in `appsettings.json` to a Postgres connection string. SQLite-specific types in any custom migration (e.g. `TEXT` for enums) may need adjustment — for the scaffolded schema this is a no-op.

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
