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

Two harnesses, two questions.

**`MyApp.Benchmarks` (BenchmarkDotNet, in-process)** — `WritePipelineBench` hosts the API via `WebApplicationFactory<Program>` and measures `POST /orders` end-to-end through middleware, model binding, mediator dispatch, validation, EF Core SaveChanges, and the outbound shipping call (stubbed). It reports allocation per request and median latency. In-process means you're measuring the *pipeline*, not the network — so the numbers are useful for spotting regressions, not for capacity planning.

**`MyApp.LoadTest` (NBomber, real Kestrel)** — drives sustained concurrency against a real Kestrel process. Two terminals: one runs the Api, the other runs the load test. NBomber reports p50/p95/p99 latency and RPS. This is where you size your service.

A caveat on the storage layer: the template ships SQLite-in-WAL because it's frictionless to scaffold. Read-heavy benchmarks are honest — WAL handles concurrent readers well. **Write-heavy benchmarks need PostgreSQL** for production-grade numbers; SQLite serialises writers and will under-report what your real stack can do. (The Mapperly LINQ-fallback comparison from ZA.Mapping's benchmark suite isn't relevant here — the template's BDN measures *its own* pipeline, not a vs-comparison.)

### Results

Measured on a 2022 i9-12900HK / Windows 11 / .NET 10.0.7. Single run; reproduce on your own hardware for capacity planning.

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

### Reproduction

```bash
# Allocation per request (BDN, in-process)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# RPS under load (NBomber, real Kestrel). Stub the shipping client so the
# seed step doesn't DNS-fail against the placeholder shipping URL.
Shipping__UseStub=true dotnet run --project src/MyApp.Api &
dotnet run -c Release --project benchmarks/MyApp.LoadTest
```

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
