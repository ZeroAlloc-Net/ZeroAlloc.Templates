# `za-vertical-slice` Template — Design

**Date:** 2026-05-26
**Scope:** New template `za-vertical-slice` under `content/` in the existing `ZeroAlloc.Templates` NuGet pack. Ships as the second template alongside the already-shipped `za-clean` (2026-05-10). Same 10-package showcase depth as `za-clean`; structurally inverted — no horizontal layers, one folder per use case, one file per slice. Bumps `ZeroAlloc.Templates` to 0.4.0 (additive minor).

## Background

`ZeroAlloc.Templates` shipped its first template (`za-clean`) on 2026-05-10. The scaffold convention is `content/<short-name>/` with one `.template.config/template.json` per template; all templates ship in the same nupkg. New templates land as `content/<name>/` siblings without touching `za-clean`.

The org-wide backlog identified five unblocked templates as of today (`za-vertical-slice`, `za-cqrs-es`, `za-blazor-wasm`, `za-blazor-server`, `za-modular`). `za-vertical-slice` is the natural next pick: closest in scope to `za-clean`, sharpest A/B comparison value for readers, and uses no packages beyond what `za-clean` already showcases.

Vertical Slice Architecture (Jimmy Bogard, 2014) organizes code around use cases rather than technical layers. Each slice owns its request/response, validator, handler, endpoint, and (often) its persistence entity. Cross-cutting concerns flow through pipeline behaviours; there are no horizontal `Application/`, `Infrastructure/`, `Domain/` projects. The pedagogical value: a reader sees an entire feature top-to-bottom in one file.

## Goal

Ship `dotnet new za-vertical-slice -n MyApp` that scaffolds a production-shaped Web API using the same 10 ZeroAlloc packages `za-clean` ships, but arranged as a vertical-slice solution: one src project, feature folders, one file per slice. Doubles as a side-by-side reference next to `za-clean` so readers can compare the same feature expressed under both architectures.

## Decisions

### D-1: depth — parity with `za-clean`

Same 10 ZA packages, same sample domain (Orders, plus Customers to demonstrate multi-area arrangement), same persistence (EF Core 10 + Microsoft.Data.Sqlite, WAL + `synchronous=NORMAL`), same showcase benchmarks (BenchmarkDotNet + NBomber), same authorization pattern, same CI smoke-test gates.

**Why:** A leaner template would be cheaper to ship but undermines the A/B comparison value. A reader comparing `za-clean` to `za-vertical-slice` should see identical functional scope, identical package coverage, and **only** the architectural arrangement differing. Anything else dilutes the comparison.

**Considered and rejected:**

- **Leaner v0.x cut** (3 packages: Mediator + Validation + Results). Faster to ship but fails the A/B promise.
- **Mid-cut** (7 packages, dropping Authorization + Resilience + Rest). Same problem at smaller scale.

### D-2: project shape — single src project

`src/MyApp/MyApp.csproj` is the only src project. No `MyApp.Application`, no `MyApp.Domain`, no `MyApp.Infrastructure`. Tests + benchmarks remain separate projects (their separation is orthogonal to the application layout).

**Why:** Pure expression of the vertical-slice idiom. Maximally contrasts with `za-clean`'s 4-csproj layered structure. A `MyApp.Contracts` library can be added when a real consumer needs it; the template demonstrates the default.

**Considered and rejected:**

- **Two projects (app + `MyApp.Contracts`).** Legitimate practical pattern but doesn't differentiate the template from `za-clean`. Add when needed.
- **Multi-project per slice** (e.g., `MyApp.Features.Orders.csproj`). Per-area csproj. Rare in practice; contradicts vertical-slice's low-ceremony promise.

### D-3: slice file organization — one file per slice

`Features/<Area>/<UseCase>/<UseCase>.cs` contains the request/response record, validator, handler, endpoint mapping, and (where applicable) the persistence entity. Single file, top-to-bottom feature view. Canonical Bogard pattern.

**Why:** This is the *visual signal* that defines vertical slice. A reader who scrolls through `PlaceOrder.cs` sees the entire feature — validation rules, business logic, persistence, response shape — in one ~50-100 line file. Splits across multiple files would be "horizontal layers within a vertical slice folder" — diluted ideology and lost pedagogical clarity.

Real-world slices that outgrow one file are a signal the slice should be *split into smaller slices*, not that the file should be split horizontally. Document this rule in `Features/README.md`.

**Considered and rejected:**

- **Multi-file slices** (`{Request.cs, Validator.cs, Handler.cs, Endpoint.cs}` per folder). Conventional but reintroduces horizontal layering at slice granularity.
- **Hybrid** (single-file default; multi-file when complex). The "when complex" rule is vague and erodes the convention's signal. Better to keep single-file strict and let users diverge per-project if they need to.

## Design

### Repo layout (additive — no changes to `za-clean`)

```
ZeroAlloc.Templates/
├── content/
│   ├── za-clean/                         ✅ unchanged
│   └── za-vertical-slice/                ← NEW
│       ├── .template.config/template.json
│       ├── MyApp.slnx
│       ├── Directory.Build.props
│       ├── Directory.Packages.props
│       ├── global.json
│       ├── AGENTS.md
│       ├── CLAUDE.md
│       ├── README.md                     ← template-level walkthrough (lives in scaffolded apps)
│       ├── src/MyApp/
│       │   ├── MyApp.csproj
│       │   ├── Program.cs                ← Minimal API host, endpoint discovery walk
│       │   ├── Features/
│       │   │   ├── README.md             ← "one file per slice" convention
│       │   │   ├── Orders/
│       │   │   │   ├── PlaceOrder/PlaceOrder.cs
│       │   │   │   ├── GetOrder/GetOrder.cs
│       │   │   │   ├── ListOrders/ListOrders.cs
│       │   │   │   └── CancelOrder/CancelOrder.cs
│       │   │   └── Customers/
│       │   │       ├── CreateCustomer/CreateCustomer.cs
│       │   │       └── GetCustomer/GetCustomer.cs
│       │   ├── Persistence/
│       │   │   ├── AppDbContext.cs
│       │   │   └── Migrations/           ← committed; generated at template-build time
│       │   ├── Common/
│       │   │   ├── ValueObjects.cs       ← [TypedId] OrderId, CustomerId
│       │   │   ├── Errors.cs             ← shared Error catalog
│       │   │   └── Telemetry.cs          ← OTel resource setup
│       │   └── Authorization/
│       │       └── Policies.cs           ← [Policy] declarations
│       ├── tests/
│       │   ├── MyApp.UnitTests/
│       │   ├── MyApp.IntegrationTests/
│       │   └── MyApp.ConventionTests/    ← renamed from za-clean's ArchitectureTests
│       └── benchmarks/
│           ├── MyApp.Benchmarks/
│           ├── MyApp.Benchmarks.Primitives/
│           └── MyApp.LoadTest/
└── tests/
    └── ZeroAlloc.Templates.SmokeTests/    ← extend to include za-vertical-slice install + scaffold + build
```

### Slice file shape (canonical example — `PlaceOrder/PlaceOrder.cs`)

```csharp
using FluentValidation;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Authorization;

namespace MyApp.Features.Orders.PlaceOrder;

[RequirePolicy("customer")]
public readonly record struct PlaceOrderCommand(CustomerId CustomerId, decimal Total) : IRequest<Result<OrderId, Error>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.Total).GreaterThan(0).WithMessage("Total must be positive");
    }
}

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    private readonly AppDbContext _db;
    public PlaceOrderHandler(AppDbContext db) => _db = db;

    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(OrderId.New(), cmd.CustomerId, cmd.Total);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order.Id;
    }
}

public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(cmd, ct);
            return result.Match(id => Results.Created($"/orders/{id}", id), err => err.ToProblem());
        });
}

// Persistence entity colocated with the slice that creates it.
internal sealed class Order
{
    public OrderId Id { get; }
    public CustomerId CustomerId { get; }
    public decimal Total { get; }

    public Order(OrderId id, CustomerId customerId, decimal total) =>
        (Id, CustomerId, Total) = (id, customerId, total);
}
```

~50 lines, one file, no horizontal layering, complete feature.

### Endpoint discovery

`Program.cs` walks the app assembly at startup to find every `public static class *Endpoint` with a `public static void Map(IEndpointRouteBuilder)` method, and calls each:

```csharp
foreach (var endpointType in typeof(Program).Assembly
    .GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Endpoint"))
    .Where(t => t.GetMethod("Map", new[] { typeof(IEndpointRouteBuilder) }) is not null))
{
    endpointType.GetMethod("Map")!.Invoke(null, new object[] { app });
}
```

Runtime walk, not source-generated. v0.4 acceptable — performance non-critical, replaceable later. `IsAbstract && IsSealed` is the C# encoding of `static class`.

### Packages pre-wired (10 — parity with za-clean)

Mediator, Validation (FluentValidation pipeline behavior), Results, Mapping (only in slices that project to DTOs), ValueObjects (`[TypedId]` `OrderId`/`CustomerId`), Inject (DI registration), Authorization (`[RequirePolicy]` + `Authorization/Policies.cs`), Telemetry (OTel resource + slice-level activities through Mediator pipeline behaviour), Resilience (Polly on outbound HTTP via Rest), Rest (one typed `[Rest]` client demonstrating outbound API integration).

### Persistence

EF Core 10 + `Microsoft.Data.Sqlite`. WAL + `synchronous=NORMAL` connection-string opts. `AppDbContext` is the only shared persistence type; slices reference it directly. Migrations committed under `Persistence/Migrations/` (generated at template-build time).

### Testing

| Project | Purpose |
|---|---|
| `MyApp.UnitTests` | Handler tests per slice. xUnit + in-memory `AppDbContext` via SQLite `:memory:`. Test naming follows slice naming (`PlaceOrderHandlerTests.WithValidInput_PersistsAndReturnsOrderId`). |
| `MyApp.IntegrationTests` | `WebApplicationFactory<Program>` exercising the full HTTP pipeline. One happy-path + one sad-path test per slice. |
| `MyApp.ConventionTests` | Renamed from za-clean's `ArchitectureTests`. NetArchTest rules adapted: (1) every `Features/*/*/` slice folder contains exactly one `*.cs` file; (2) no slice references another slice's internal types; (3) every command/query is named `*Command` or `*Query` and implements `IRequest<T>`; (4) every handler is `sealed public`. Rules that enforced "Domain doesn't reference Application" are dropped (no such layers). |

### Benchmarks (parity with za-clean)

- `MyApp.Benchmarks` — BenchmarkDotNet scenario invoking the `PlaceOrder` slice handler through `WebApplicationFactory.CreateClient()` (in-process). Demonstrates same allocation profile as za-clean's identical scenario.
- `MyApp.Benchmarks.Primitives` — ZA primitive micro-benchmarks (TypedId construction, Result allocation, etc.). Mirrors za-clean.
- `MyApp.LoadTest` — NBomber against Kestrel on loopback. Same shape.

### Template config (`.template.config/template.json`)

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Marcel Roozekrans",
  "classifications": ["Web", "API", "Vertical Slice", "ZeroAlloc"],
  "identity": "ZeroAlloc.Templates.VerticalSlice",
  "name": "ZeroAlloc Vertical Slice Web API",
  "shortName": "za-vertical-slice",
  "tags": { "language": "C#", "type": "solution" },
  "sourceName": "MyApp",
  "preferNameDirectory": true,
  "symbols": {
    "EnableSwagger": { "type": "parameter", "datatype": "bool", "defaultValue": "true" },
    "SeedDatabase": { "type": "parameter", "datatype": "bool", "defaultValue": "true" },
    "IncludeDocker": { "type": "parameter", "datatype": "bool", "defaultValue": "false" }
  }
}
```

Same parameters as `za-clean` for cross-template consistency.

### Sample domain — Orders + Customers

`za-clean` has Orders only. `za-vertical-slice` adds a second area (Customers) to demonstrate multi-area slice arrangement and shared infrastructure (`AppDbContext`, `[TypedId]` value objects). Slice count: 6 (PlaceOrder, GetOrder, ListOrders, CancelOrder, CreateCustomer, GetCustomer).

### CI smoke tests

Pattern from `za-clean`:

1. Build the local `ZeroAlloc.Templates.nupkg`.
2. `dotnet new install ./nupkg/ZeroAlloc.Templates.X.Y.Z.nupkg`.
3. `dotnet new za-vertical-slice -n SmokeTest` in a tempdir.
4. `dotnet restore + build + test` on the scaffolded solution.
5. Quick BDN run (`--quick`) to surface startup-path regressions.

Lives next to the existing za-clean smoke job in `.github/workflows/ci.yml`. Both jobs run on every PR.

## Out of scope (deferred)

- **Source-gen for endpoint registration.** v0.4 uses runtime assembly walk. A small generator can replace this in a follow-up if startup time becomes a concern (current `WebApplicationFactory` benchmarks suggest it isn't).
- **Cross-slice messaging idiom.** The template documents (in `Features/README.md`) the rule "if you need cross-slice messaging, use `IMediator.Publish` notifications — never type-reference another slice." It does NOT demonstrate this with a concrete cross-slice notification example, to keep the slice count small and the idiom clean.
- **Modular monolith pattern.** Different template (`za-modular`) when graduated.
- **AOT-published variant.** ASP.NET Core minimal APIs + EF Core + Mediator runtime dispatch don't yet AOT-publish cleanly across this whole stack. Defer until the ecosystem catches up.
- **Per-slice DTO mapping showcase.** `za-clean` ships `Order → OrderDto` projections via `ZeroAlloc.Mapping`. The vertical-slice template includes the same packages but the example slices project directly to records to keep file size small. `GetOrder/GetOrder.cs` demonstrates Mapping with a single projection.

## Backward compatibility

Strictly additive. No changes to `za-clean`. No changes to the `ZeroAlloc.Templates` csproj beyond adding the new `content/za-vertical-slice/` folder to the pack. SemVer: minor bump (`0.3.x` → `0.4.0`).

## Files touched

- **NEW:** `content/za-vertical-slice/` — entire template tree (~70-90 .cs files: 6 slices + persistence + common + auth + Program + tests + benchmarks).
- **MOD:** `ZeroAlloc.Templates.csproj` — confirm the pack glob already includes `content/**/*` (likely yes; no change needed). Add a `<Content>` entry only if the existing pack rules require explicit listing.
- **MOD:** `.github/workflows/ci.yml` — add the new template's smoke-test job.
- **MOD:** `README.md` (repo-level) — list `za-vertical-slice` in the template catalog.
- **MOD:** `CHANGELOG.md` — auto-generated by release-please.
- **MOD:** `docs/plans/2026-05-26-za-vertical-slice.md` (the implementation plan, written next).

Plus org-wide `c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md` — mark `za-vertical-slice` ✅ shipped post-release (workspace-local edit).
