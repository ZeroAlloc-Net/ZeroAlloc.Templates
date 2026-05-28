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
| [ZA.Validation](https://validation.zeroalloc.net) | `MyApp.Application/CreateOrder/CreateOrderCommand.cs`, `OrderItem.cs` | `[Validate]` attributes on the command + nested item record. ZA.Validation's source generator emits `CreateOrderCommandValidator` and `OrderItemValidator` at build time. `[NotEmpty]` on `IReadOnlyList<OrderItem>` covers the "at least one item" rule via the type-aware emission introduced in 1.3.0. The thin facade at [`CreateOrderValidator.cs`](../content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderValidator.cs) maps the first failure to `ApplicationError("validation.failed", ...)` so `CreateOrderHandler.Handle` stays unchanged. |
| [ZA.Mediator](https://mediator.zeroalloc.net) | All handlers in `MyApp.Application/*` | `IRequest<TResponse>` / `IRequestHandler<TRequest, TResponse>`. Handlers return `ValueTask<T>`. ActivitySource `ZeroAlloc.Mediator` is wired into OTel. |
| [ZA.Inject](https://inject.zeroalloc.net) | `EfOrderRepository`, `ShippingQuoteHttpClient`, all handlers | `[Scoped]` / `[Singleton]` / `[Transient]` attributes — **not** `[Service(ServiceLifetime.X)]`. Generated `AddMyAppApplication()` / `AddMyAppInfrastructure(...)` extensions compose registration. |
| [ZA.Authorization](https://authorization.zeroalloc.net) + [ZA.Mediator.Authorization](https://mediator.zeroalloc.net) | `MyApp.Application/Authorization/OrdersPolicies.cs`, `MyApp.Api/Authorization/HttpSecurityContextAccessor.cs` | `[AuthorizationPolicy("OrdersRead"/"OrdersWrite")]` defines two policies that read the JWT `scope` claim (RFC 6749 space-separated tokens). Commands and queries carry `[Authorize(...)]`. The mediator pipeline behavior runs the policy against an `ISecurityContext` bridged from `HttpContext.User` and denies dispatch before the handler executes — **defense in depth on top of** the endpoint-level `RequireAuthorization` policies (same names, same claims) registered on the minimal-API routes. |
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

Measured in CI on GitHub Actions ubuntu-latest (AMD EPYC, .NET 10.0.8) via the `Benchmarks (manual)` workflow on 2026-05-28 — `gh workflow run benchmarks.yml`. Single run; reproduce on your own hardware for capacity planning.

#### Primitives — ZA framework cost in isolation

Measured with `MyApp.Benchmarks.Primitives` — no ASP.NET, no EF, just the ZA packages. These are the numbers that deliver on the "zero-allocation through the framework hot path" claim.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS
AMD EPYC 7763, .NET SDK 10.0.300, .NET 10.0.8 X64 RyuJIT x86-64-v3
```

| Method                    | Mean       | Error     | StdDev    | Gen0   | Allocated |
|-------------------------- |-----------:|----------:|----------:|-------:|----------:|
| `Mapping_RequestToCommand`|  45.090 ns | 0.2784 ns | 0.2325 ns | 0.0157 |     160 B |
| `Mediator_DispatchOnly`   |  28.870 ns | 0.0447 ns | 0.0396 ns |      - |       0 B |
| `Validator_HandRolled`    | 111.922 ns | 0.1563 ns | 0.1220 ns |      - |       0 B |
| `ValueObject_TryCreate`   |   3.355 ns | 0.0082 ns | 0.0064 ns |      - |       0 B |
| `EndToEndPrimitives`      | 216.436 ns | 0.7865 ns | 0.7357 ns | 0.0312 |     160 B |

The decisive datapoint: `EndToEndPrimitives` matches `Mapping_RequestToCommand` byte-for-byte (both 160 B). The validator + mediator dispatch through the chain allocate **zero bytes**. The 160 B is the `CreateOrderCommand` record + nested `OrderItem[]` array — caller cost every framework pays, not ZA overhead.

Compare with the full-pipeline `WritePipeline` row below: that ~158 KB is ASP.NET model binding + JSON + EF tracking, not ZA framework cost. Use the primitives table for "does the framework allocate", the pipeline table for "does the endpoint allocate".

#### Full pipeline (ASP.NET + EF Core in the mix)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS
AMD EPYC 7763, .NET SDK 10.0.300, .NET 10.0.8 X64 RyuJIT x86-64-v3
```

| Method        | Mean     | Error     | StdDev    | Gen0    | Allocated |
|-------------- |---------:|----------:|----------:|--------:|----------:|
| WritePipeline | 1.093 ms | 0.0308 ms | 0.0832 ms | 15.6250 | 157.58 KB |

The 157.58 KB is dominated by ASP.NET Core's request pipeline (model binding, JSON deserialization, response shaping) and EF Core's tracking buffer — not the ZA framework cost. The handler-level allocation (mapping + Mediator dispatch + Result construction) is in the low hundreds of bytes; the rest is HTTP plumbing every endpoint pays. Use this as a regression baseline, not a capacity-planning number. A Postgres-backed bench profile is filed as a backlog item — SQLite's single-file lock dominates the wall-clock budget under the current setup.

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
_Imported from ZA.Mapping — last refreshed 2026-05-25._

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
_Imported from ZA.Mediator — last refreshed 2026-05-25._

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

**Stream allocation source.** The 104 B on the ZA Stream row is the C# compiler's `async IAsyncEnumerable<T>` state-machine allocation for the user's handler method (e.g. `async IAsyncEnumerable<int> Handle(...) { yield return ...; }`). Any mediator dispatching to such handlers pays this cost — MediatR pays it *on top of* its own ~424 B wrapper. ZA.Mediator's own stream-dispatch contribution is 0 B. .NET 10 has no pooled state-machine builder for `async IAsyncEnumerable<T>` (unlike `ValueTask` with `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]`); the only way to drop below 104 B is to implement `IAsyncEnumerable<T>` by hand without `yield return`.
<!-- MEDIATOR:END -->

#### Validation
<!-- VALIDATION:START -->
_Imported from ZA.Validation — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

| Scenario | ZA valid | FluentValidation valid | Speedup | ZA alloc (valid) | FV alloc (valid) |
|---|---:|---:|---:|---:|---:|
| Flat | 6.7 ns | 327 ns | **~49×** | **0 B** | 664 B |
| Nested | 10.1 ns | 619 ns | **~61×** | **0 B** | 1,488 B |
| Collection (3 items) | 14.3 ns | 2,043 ns | **~143×** | **0 B** | 3,456 B |

ZeroAlloc.Validation is **49–143× faster** than FluentValidation on the valid path with **zero heap allocation**. On the invalid path it's 31–56× faster and allocates 10–18× less. The gap widens as the model shape grows — FluentValidation's per-call `List<ValidationFailure>` and cached expression-tree delegates pay a larger fixed cost as more rules fire.
<!-- VALIDATION:END -->

#### Inject
<!-- INJECT:START -->
_Imported from ZA.Inject — last refreshed 2026-05-25._

_Last refreshed: 2026-05-18_

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 6,413 ns | 6,944 B |
| ZA.Inject Container — `BuildZeroAllocInjectServiceProvider()` | **1,034 ns** | **2,664 B** |
| ZA.Inject Standalone — `new …StandaloneServiceProvider()` | **8 ns** | 48 B |
| Jab — `new JabContainer()` | 8 ns | **40 B** |

The Container row dropped from 10,998 ns / 11,192 B (v1.6 and earlier) to 1,034 ns / 2,664 B in v1.7: `BuildZeroAllocInjectServiceProvider()` now snapshots the `IServiceCollection` and **lazy-builds the MS DI fallback `IServiceProvider` on first need** via `Interlocked.CompareExchange`. Applications whose registrations are fully ZA-owned (the common case for `[Singleton]`/`[Transient]`/`[Scoped]`-attributed services) never pay the `BuildServiceProvider()` cost at all. The MS DI baseline above was also corrected — it previously returned `IServiceCollection` without calling `BuildServiceProvider()`, making the comparison meaningless.

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
| Create scope | 82 ns / 128 B | **60 ns / 96 B** | 58 ns / 88 B | **14 ns / 40 B** | — |
| Resolve scoped (full lifecycle) | 7,181 ns / 304 B | 5,901 ns / 120 B | 4,851 ns / 120 B | **5,216 ns / 120 B** | — |

_¹ Jab is constructor-only — no property injection._
_² Jab decorator wired via factory (no first-class decorator attribute)._
_³ Jab 0.10.x requires closed types at the `[ServiceProvider]` attribute level._

ZA.Inject is **competitive across every scenario** and the clear winner where the generator's domain knowledge matters most: property injection (2× MS DI), decorators (2.1× MS DI), open generics (1.8× MS DI). Jab leads on scope creation (its scope is the lightest of the four, by an order of magnitude), with ZA Standalone close behind on the full scoped-resolution lifecycle.

In v1.6 the **ZA.Inject Container** scope creation dropped from 123 ns / 216 B to **60 ns / 96 B** — the MS DI fallback scope is no longer eagerly materialized; it's created on first fallback resolution via `Interlocked.CompareExchange`. For applications whose registrations are fully ZA-owned (the common case), the fallback scope is never created at all. **`IEnumerable<T>` resolutions are also cached** when every entry is `Singleton` (verified by `EnumerableCacheTests`); the benchmark row above shows the all-`Transient` registration path, which remains genuinely allocation-bound — the new cache fires for the singleton-multi-impl scenario.
<!-- INJECT:END -->

#### Results
<!-- RESULTS:START -->
_Imported from ZA.Results — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

| Scenario | ZeroAlloc.Results | OneOf | ErrorOr | FluentResults |
|---|---:|---:|---:|---:|
| Success construct | 0.4 ns / 0 B | 0.5 ns / 0 B | 0.0 ns / 0 B | 87 ns / **112 B** |
| Failure construct | 0.3 ns / 0 B | 0.9 ns / 0 B | 63 ns / **184 B** | 87 ns / **272 B** |
| Success consume | 0.3 ns / 0 B | 0.1 ns / 0 B | 0.2 ns / 0 B | 75 ns / **96 B** |
| Failure consume | 0.4 ns / 0 B | 0.9 ns / 0 B | 2.6 ns / 0 B | 214 ns / **240 B** |
| Hot loop (100 iter, mixed) | **183 ns / 0 B** | 202 ns / 0 B | 7,693 ns / 6,256 B | 39,450 ns / 25,968 B |

ZeroAlloc.Results is the **only library with 0 B allocation on every path** — including failure construction, which is where ErrorOr (184 B) and FluentResults (272 B) pay the most. OneOf is the closest competitor (also struct-based, also 0 B on hot paths) but is ~10% slower on the realistic mixed workload.

**The realistic-workload headline (100 iterations with 1-in-3 failures):**

- ZeroAlloc.Results: **183 ns / 0 B**
- OneOf: 202 ns / 0 B (1.1× slower)
- ErrorOr: 7,693 ns / 6,256 B (**42× slower, +∞× more alloc**)
- FluentResults: 39,450 ns / 25,968 B (**216× slower, +∞× more alloc**)

ErrorOr and FluentResults allocate per-failure because their error types (`Error` struct with description string interning + `IError` interface implementations) are non-trivial. For a CRUD app handling occasional validation errors the cost is invisible; for a hot pipeline processing tens of thousands of items where any non-trivial fraction fail, it dominates.
<!-- RESULTS:END -->

#### ValueObjects
<!-- VALUEOBJECTS:START -->
_Imported from ZA.ValueObjects — last refreshed 2026-05-25._

_Last refreshed: 2026-05-18_

| Operation | Vogen | ZA.ValueObjects | Winner |
|---|---:|---:|---|
| `From(value)` | 4.66 ns | **0.39 ns** | **ZA 12× faster** |
| `Equals` (equal) | 1.15 ns | **0.09 ns** | **ZA 13× faster** |
| `Equals` (not equal) | 0.31 ns | **0.02 ns** | **ZA 15× faster** |
| `GetHashCode` | 0.03 ns | 0.42 ns | parity (both in BDN ZeroMeasurement zone) |
| `ToString` | 6.40 ns | **3.52 ns** | **ZA 1.8× faster** |

Both libraries are 0 B on every row. ZA wins the hot-path operations (`From`, `Equals`) by a wide margin — Vogen's `From` pays validation overhead even when validation succeeds. `GetHashCode` is effectively a tie (both rows sit in BDN's ZeroMeasurement zone — "indistinguishable from empty method"). `ToString` is now ZA's win after the single-property generator emit was aligned: the generator emits `Value.ToString(CultureInfo.InvariantCulture)` directly instead of the previous record-wrapped `$"TypeName {{ Value = {Value} }}"` interpolation.

**The trade-off**: ZA optimises construction, equality, and now `ToString` and hashing alongside Vogen. Vogen's narrower wrapping (single primitive) and ZA's broader surface (multi-field, custom types, EF Core converters) make them complementary choices — pick by feature surface, not raw single-int benchmark numbers.

History: the previous single-property `ToString` allocated ~72 B per call and `GetHashCode` was ~30× slower than Vogen, both fixed in ZeroAlloc.ValueObjects v1.7 by emitting bare `Value.ToString(InvariantCulture)` / `Value.GetHashCode()` for 1-property `[ValueObject]` types. Multi-property types are unchanged.
<!-- VALUEOBJECTS:END -->

#### Specification
<!-- SPECIFICATION:START -->
_Imported from ZA.Specification — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

Ardalis.Specification is the most-used specification library in .NET — class-based, designed for EF Core query composition. ZA.Specification's struct-based design pays off across every operation that exercises both libraries' core surface.

| Operation | Ardalis.Specification | ZA.Specification | Speedup |
|---|---:|---:|---:|
| Construct composed spec | 1,719 ns / 1,248 B | **40 ns / 24 B** | **43× faster, 52× less alloc** |
| Compose two specs | 3,959 ns / 2,648 B | **22 ns / 24 B** | **180× faster, 110× less alloc** |
| Evaluate over 100 items (in-memory) | 170,862 ns / 4,688 B | **150 ns / 0 B** | **1,136× faster, ∞× less alloc** |

The in-memory-evaluation gap is dramatic because Ardalis is designed for **IQueryable composition** — `WhereExpression.Filter.Compile()` is hundreds of microseconds per call. For EF Core / database queries where the SQL provider dominates the cost, this overhead is invisible. For in-memory filtering (which many users do), it dominates.

ZA.Specification's `IsSatisfiedBy` is a direct virtual call on a struct value — the JIT inlines it, the composition tree resolves to a sequence of `&&` operators in straight-line code. Zero allocation per evaluation regardless of composition depth.
<!-- SPECIFICATION:END -->

#### StateMachine
<!-- STATEMACHINE:START -->
_Imported from ZA.StateMachine — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

[Stateless](https://github.com/dotnet-state-machine/stateless) is the de-facto state-machine library in .NET — class-based, fluent configuration, runtime trigger dispatch. ZA's source-generated switch dispatch dominates every comparable scenario.

| Operation | Stateless | ZA.StateMachine | Speedup |
|---|---:|---:|---:|
| Fire valid (3-step cycle) | 4,495 ns / 7,272 B | **36 ns / 24 B** | **124× faster, 303× less alloc** |
| Fire invalid | 27 ns / 24 B | **1.6 ns / 0 B** | **17× faster, 0 B alloc** |
| Guard allowed | 2,718 ns / 4,160 B | **15 ns / 24 B** | **178× faster, 173× less alloc** |
| Guard blocked | 699 ns / 792 B | **0.3 ns / 0 B** | **2,200× faster, 0 B alloc** |

The 24 B in ZA's "valid" rows is from the per-iteration `new OrderMachine()` reset, not from `TryFire`. The Stateless row's 7,272 B is two things combined: each `Fire` walks a `Dictionary<TTrigger, StateRepresentation>` and constructs trigger/transition info objects, **and** the per-iteration `BuildStatelessOrder()` reset rebuilds the configuration (`new StateMachine<,>(initial)` plus three `Configure().Permit()` calls — each one allocating dictionary entries).

This is the apples-to-apples comparison for cyclic state machines — a per-request handler in a web app, or a fresh circuit-breaker per stream. Both libraries pay a "reset" cost in this workload. ZA's reset is one struct/class allocation because configuration is resolved at compile time; Stateless's includes the full fluent-configuration rebuild because configuration is runtime. The dispatch portion alone (excluding the rebuild) is roughly an order of magnitude smaller on the Stateless side but still measurably slower than ZA's `switch`-expression `TryFire`.
<!-- STATEMACHINE:END -->

#### Resilience
<!-- RESILIENCE:START -->
_Imported from ZA.Resilience — last refreshed 2026-05-25._

_Last refreshed: 2026-05-18_

[Polly](https://github.com/App-vNext/Polly) v8 (`ResiliencePipeline`) is the de-facto resilience library in .NET. ZA.Resilience's source-generated proxy beats it on both throughput and allocation for the policies both libraries support apples-to-apples.

| Operation | Polly v8 | ZA.Resilience | Speedup |
|---|---:|---:|---:|
| Retry, happy path | 600 ns / 64 B | **23 ns / 0 B** | **26× faster, 0 B alloc** |
| CircuitBreaker, closed | 776 ns / 64 B | **17 ns / 0 B** | **45× faster, 0 B alloc** |
| Retry with 2/3 failures | 22.86 ms / 3,134 B | 27.89 ms / 948 B | 22% slower wall-clock, **3.3× less alloc** |
| Retry with 2/3 failures, **backoff=0** (isolates loop overhead) | 12.80 µs / 1,984 B | **7.31 µs / 576 B** | **43% faster, 71% less alloc** |
| All-policies stacked, happy path | 1,283 ns / 104 B | **126 ns / 144 B** | **10× faster** |
| All-policies stacked, retry triggers (2/3 fail) | 29.31 ms | 28.83 ms | parity (Task.Delay floor) |
| All-policies stacked, CB open (fast-reject) | **3.97 µs / 40 B** | 5.16 µs / 912 B | Polly wins — see narrative below |

The happy-path gap is driven by Polly's `ResiliencePipeline.ExecuteAsync` walking the strategy chain via delegate dispatch and allocating a `ResilienceContext` per call (64 B). ZA emits one direct method per interface — the retry/CB checks are inline `if` statements and `Volatile.Read` calls. No context object, no closure, no delegate.

The retry-with-failures row at 1 ms backoff shows ZA 22% slower wall-clock. **Phase-1 investigation (backoff=0 micro-bench, 2026-05-18) confirms the retry loop itself is competitive — at `Task.Delay(0)` ZA is *faster* than Polly (7.31 µs vs 12.80 µs, 43% lower) with 71% less allocation.** The 22% gap on the 1 ms-backoff row is `Task.Delay` Windows timer-tick alignment (each `Task.Delay(1ms)` wakes ~16 ms later due to system timer resolution) plus scheduler-thread continuation handoff — both framework-bound, not ZA loop overhead.

**All-policies stacked comparison.** Three rows compare a 4-policy stack (Retry + Timeout + RateLimit + CircuitBreaker). **Happy path** measures cumulative dispatch overhead — ZA wins by ~10× because the generator emits one flat method with inline policy checks (`Volatile.Read` + integer comparisons), while Polly's `ResiliencePipeline.ExecuteAsync` walks the strategy chain with delegate dispatch. **Retry triggers** measures the most realistic prod failure mode — inner fails 2/3, retry recovers; both libraries hit the same Windows-timer-tick floor on `Task.Delay(1ms)` so wall-clock is at parity. **CB open (fast-reject)** measures the steady-state cost when the circuit is open: **Polly wins** because we explicitly excluded `BrokenCircuitException` from Polly's retry `ShouldHandle` — Polly does a single fast-reject per call. ZA's generated retry catches `ResilienceException` (which the CB raises) and retries 3× through the still-Open circuit, accumulating 912 B of state-machine allocations across the attempts. This is a real cost difference, not a measurement artifact — and arguably a correctness consideration: applications using ZA where the CB-open scenario matters should disable retry-on-CB-broken in user code, which the current generator doesn't surface as a knob (tracked for follow-up).

Rate-limit and timeout limits in the all-policies harness are set to `int.MaxValue` permits / 60s ResetMs so neither policy trips during measurement. The rate-limiter apples-to-apples comparison is deferred because the two libraries' rate-limiter implementations differ (Polly wraps `System.Threading.RateLimiting.ConcurrencyLimiter`; ZA has its own throughput-based impl).
<!-- RESILIENCE:END -->

#### Rest
<!-- REST:START -->
_Imported from ZA.Rest — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8.

| Method | Mean | Ratio | Allocated | vs Refit |
|---|---:|---:|---:|---:|
| RawHttpClient_Get | 2.09 μs | 1.00 | 1.38 KB | — |
| **ZeroAlloc_Get** | **3.52 μs** | **1.68** | **1.88 KB** | **3.6× faster** |
| Refit_Get | 12.70 μs | 6.07 | 2.88 KB | — |
| RawHttpClient_Post | 3.12 μs | 1.49 | 1.70 KB | — |
| **ZeroAlloc_Post** | **6.70 μs** | **3.20** | **2.64 KB** | **1.7× faster** |
| Refit_Post | 11.62 μs | 5.56 | 3.46 KB | — |
| RawHttpClient_QueryParam | 2.16 μs | 1.03 | 1.45 KB | — |
| **ZeroAlloc_QueryParam** | **4.28 μs** | **2.04** | **1.99 KB** | **3.6× faster** |
| Refit_QueryParam | 15.51 μs | 7.41 | 3.55 KB | — |
| RawHttpClient_Delete | 1.10 μs | 0.53 | 1.11 KB | — |
| **ZeroAlloc_Delete** | **1.92 μs** | **0.92** | **1.61 KB** | **2.4× faster** |
| Refit_Delete | 4.62 μs | 2.21 | 2.45 KB | — |
| RawHttpClient_Result | 2.04 μs | 0.98 | 1.32 KB | — |
| **ZeroAlloc_Result** | **4.07 μs** | **1.95** | **1.92 KB** | Refit lacks `Result<T>` |

ZeroAlloc.Rest is **1.7–3.6× faster than Refit** across every shape of call (GET / POST / GET-with-query / DELETE) with **1.3–1.5× less allocation**. Refit pays for reflection-based attribute scanning and expression-tree invocation on every call (6–8× over the raw `HttpClient` baseline); ZA's generated client is 1.7–3.2× over raw — closer to the floor.
<!-- REST:END -->

#### Serialisation
<!-- SERIALISATION:START -->
_Imported from ZA.Serialisation — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

### Deserialize — wrapper is thin

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 47.6 ns / 64 B | 55.2 ns / 64 B | **+16%, 0 B** |
| MessagePack | 123.9 ns / 64 B | 182.7 ns / 96 B | **+47%, +32 B** |
| System.Text.Json | 303.3 ns / 64 B | 374.5 ns / 64 B | **+23%, 0 B** |

The deserialize wrapper is a thin pass-through: same allocation (0–32 B extra), 16–47% extra time for the interface dispatch. The MessagePack extra 32 B is the `ReadOnlySpan<byte>` → array boxing for the underlying API call.

### Serialize — IBufferWriter pattern adds measurable cost

| Library | Raw | ZA wrapper | Overhead |
|---|---:|---:|---:|
| MemoryPack | 74.6 ns / 48 B | 159.7 ns / 312 B | **+114%, +264 B** |
| MessagePack | 128.1 ns / 32 B | 215.2 ns / 312 B | **+68%, +280 B** |
| System.Text.Json | 225.7 ns / 48 B | 287.7 ns / 448 B | **+27%, +400 B** |

The serialize wrapper costs more because `ISerializer<T>.Serialize` takes an `IBufferWriter<byte>` (the buffer abstraction). The 264–400 B is the `ArrayBufferWriter<byte>` instance + its internal buffer, allocated fresh per call by the benchmark.

This is the cost of the abstraction. **The wrapper is fastest when the caller pools the buffer writer** — the IBufferWriter pattern is designed for that scenario. The benchmark measures worst case (fresh writer per call); a real application that pools writers across N calls amortises the 264 B to ~0 per call.
<!-- SERIALISATION:END -->

#### Cache
<!-- CACHE:START -->
_Imported from ZA.Cache — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

L1 (in-process) cache-hit comparison. .NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8. ZA.Cache wraps `IMemoryCache`, so the relevant comparisons are: hand-rolled `GetOrCreateAsync` (the pattern ZA replaces) and [FusionCache](https://github.com/ZiggyCreatures/FusionCache) 2.0 (the de-facto third-party L1+L2 caching library).

| Library | Time | Allocated |
|---|---:|---:|
| Raw `IMemoryCache.GetOrCreateAsync` | 208 ns | 176 B |
| **ZA.Cache proxy** | **434 ns** | **160 B** |
| FusionCache | 989 ns | 112 B |

**ZA.Cache is 2.3× faster than FusionCache** with comparable allocation. The trade vs raw `IMemoryCache` is the ~2× cost of the typed `[Cache]` attribute abstraction (compile-time key building + async wrapper) — in exchange you don't write the cache-lookup boilerplate at every call site, and the key derivation is generated rather than hand-typed.

**FusionCache** is heavier because it carries L2-cache, stampede protection, and adaptive-caching infrastructure even when only L1 is configured. For pure L1, ZA is the lighter choice; FusionCache's value is the L2 + advanced features that ZA does not implement.

**Caveat on the raw row**: ZA's 2× premium over raw `IMemoryCache.GetOrCreateAsync` reflects the proxy dispatch + generated key composition. The raw row's 176 B allocation is the `(string, int)` tuple boxing the test uses for the key; ZA's 160 B is the generated `customer-42` string interpolation. Allocation parity is by design — both store roughly the same key shape.
<!-- CACHE:END -->

#### Telemetry
<!-- TELEMETRY:START -->
_Imported from ZA.Telemetry — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

The realistic alternative to ZA.Telemetry's generator is hand-written `ActivitySource` + `Meter` wrapping — wrapping every instrumented method with `using var activity = source.StartActivity(...); counter.Add(1); histogram.Record(...)`. This benchmark compares the two in the no-listeners profile (the common production case after sampling).

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.4.

| Method | Time | Allocated |
|---|---:|---:|
| Direct call (no instrumentation) | 87 ns | 72 B |
| Hand-written `ActivitySource` + `Counter` + `Histogram` | 201 ns | 72 B |
| **ZA.Telemetry generated proxy** | **201 ns** | **72 B** |

ZA.Telemetry's generator produces code **at parity with hand-written instrumentation** (within measurement noise; 0.04% delta). The 72 B in all three rows is the `Task<int>` allocation from the async/await pattern, not from instrumentation. Both wrappers add ~115 ns over the direct call in the no-listeners path — the `Activity.StartActivity` null-check, the `Counter.Add`, and the `Histogram.Record` — all unavoidable if you want the spans + metrics available when a sampler does subscribe.

**The takeaway**: ZA.Telemetry's value isn't faster instrumentation than hand-writing it — it's eliminating the boilerplate so every instrumented method gets the same try/finally + counter + histogram pattern, with zero risk of forgetting to dispose an Activity or skipping a metric.
<!-- TELEMETRY:END -->

#### Notify
<!-- NOTIFY:START -->
_Imported from ZA.Notify — last refreshed 2026-05-25._

_Last refreshed: 2026-05-13_

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.8. All four libraries are configured with 5 attached handlers to mirror a realistic MVVM scenario.

| Library | Time | Allocated | Async support |
|---|---:|---:|:---:|
| Manual `INotifyPropertyChanged` (baseline) | 33.6 ns | 24 B | ❌ |
| PropertyChanged.Fody | **30.2 ns** | **0 B** | ❌ |
| CommunityToolkit.Mvvm | 55.2 ns | 0 B | ❌ |
| **ZeroAlloc.Notify** | **124.7 ns** | **80 B** | ✅ |

**Honest framing**: ZA.Notify is the slowest of the four and the only one that allocates. The 80 B is the `ValueTask` state machine for fan-out to async handlers. The other three are pure sync and can't `await` propagation.

**ZA.Notify is the only library here that supports async handlers.** The trade-off is the cost of that capability. For pure-sync view models that never need to `await` a handler (the vast majority of XAML/Avalonia/WinUI scenarios), **Fody is the right choice** — it's fastest and 0 B. For scenarios where the setter needs to wait for async work to complete before returning (e.g., async validation before notifying observers; coordinated async state transitions), ZA.Notify is the only library that supports it.

**Per-iteration scale**: even at 124.7 ns / 80 B per setter call, ZA.Notify can run **~8M property changes per second per thread** with ~640 MB/s of GC pressure. Most MVVM workloads have property change rates in the thousands per second, where the difference is invisible.
<!-- NOTIFY:END -->

#### Scheduling
<!-- SCHEDULING:START -->
_Imported from ZA.Scheduling — last refreshed 2026-05-25._

_Last refreshed: 2026-05-14_

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.4. Coravel 5.0.4 (the de-facto in-process scheduler in .NET) and a hand-rolled `BackgroundService + Timer` baseline (the pattern most apps reach for before adopting a library).

### vs Coravel: registration cost

| Operation | Coravel | ZA.Scheduling | Coravel allocation |
|---|---:|---:|---:|
| Single `Schedule()` / `[Job]` registration | 8,211 ns | **compile-time, 0 ns runtime** | 696 B per call |
| 100 schedule calls (queue accumulation) | 80,217 ns / 77,232 B | **compile-time, 0 ns runtime** | 110× the per-call cost |

**Honest reading**: Coravel's `Schedule()` is a runtime call that allocates an entry the scheduler walks every tick. ZA.Scheduling's `[Job]` registration happens at compile time — the source generator emits one direct dispatcher per attributed type. There is no equivalent runtime registration call in ZA, so the comparison is between Coravel's per-call registration cost and ZA's no-cost-at-all (the cost moved to build time). The "8,211 ns / 696 B per Schedule" is the realistic cost in a Coravel app that schedules jobs dynamically at startup.

### vs naive `BackgroundService + Timer` baseline: dispatch overhead

| Operation | Naive direct call | ZA.Scheduling `IJob.ExecuteAsync` |
|---|---:|---:|
| Single dispatch | 0.01 ns | 0.25 ns |

Both rows are in BDN's "ZeroMeasurement" range (warning: "indistinguishable from empty-method duration"). **ZA.Scheduling adds no measurable overhead over a direct method call** — the `[Job]`-attributed proxy compiles to a direct return for synchronous completions, and BDN's JIT inlines both bodies entirely.

The takeaway: if you're considering ZA.Scheduling over a hand-rolled `Timer`, the dispatch cost is identical. The value of the abstraction is the `[Job]` attribute itself (declarative retries, cron, store-backed persistence) — not raw dispatch speed.
<!-- SCHEDULING:END -->

#### Outbox
<!-- OUTBOX:START -->
_Imported from ZA.Outbox — last refreshed 2026-05-25._

_Last refreshed: 2026-05-14_

Correctness-matched comparison: both rows use the **same SQLite-in-memory connection** (different tables), both wrap each enqueue in a transaction, both run a claim-then-dispatch-then-commit cycle on the dispatch tick. Storage variance cancels out — the remaining delta is the cost of the `IOutboxWriter<T>` + `IOutboxStore` + serializer abstraction layer.

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.4.

| Operation | Hand-rolled | ZA.Outbox | Overhead |
|---|---:|---:|---:|
| Enqueue (1 message, transactional) | 6.86 µs / 2.08 KB | **6.99 µs / 2.13 KB** | +2% time, +2% alloc |
| Dispatch tick (10 messages) | 105.4 µs / 11.9 KB | **115.0 µs / 11.09 KB** | +9% time, **−7% alloc** |

**Honest reading**: ZA.Outbox adds **near-zero abstraction overhead** vs writing the same correctness-matched SQLite outbox by hand. The 2–9% wall-clock delta is the `IOutboxWriter<T>` + `IOutboxStore` interface dispatch + serializer call overhead. Memory allocation is within ±5% of hand-rolled, sometimes lower.

The value of the abstraction is the `[OutboxMessage]` attribute + the typed writer + composability with other ZA packages (dispatcher, dashboard, resilience, telemetry bridges) — paid for at near-no runtime cost.
<!-- OUTBOX:END -->

#### EventSourcing
<!-- EVENTSOURCING:START -->
_Imported from ZA.EventSourcing — last refreshed 2026-05-25._

_Last refreshed: 2026-05-14_

Correctness-matched comparison: both rows use the **same SQLite-in-memory connection** (different tables), both check the current stream version inside a transaction before INSERT, both run an ordered SELECT across the stream on read. Storage variance cancels out — the remaining delta is the cost of the `IEventStore` + `IEventStoreAdapter` + `IEventSerializer` + `IEventTypeRegistry` abstraction layer.

To isolate the abstraction overhead from real-world serialization cost (which both sides would pay equally in production), the ZA row uses a cached no-op serializer. The hand-rolled row similarly stores raw bytes without round-tripping JSON.

.NET 8.0.26, i9-12900HK, BenchmarkDotNet v0.15.8.

| Operation | Hand-rolled | ZA.EventSourcing | Overhead |
|---|---:|---:|---:|
| Append (1 event, transactional, OCC check) | 80.7 µs / 3.80 KB | **106.3 µs / 4.79 KB** | +33% time, +26% alloc |
| Read 100-event stream (ordered) | 66.0 µs / 11.95 KB | **140.9 µs / 25.23 KB** | +114% time, +111% alloc |

**Honest reading**: ZA.EventSourcing adds **a measurable abstraction tax** over a raw SQLite event store — about 33% time on append and ~2× on read. That tax buys you:

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
