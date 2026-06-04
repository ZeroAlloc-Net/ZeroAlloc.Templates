# Flat Read Model for `GET /orders/{id}` — Design

**Status:** approved 2026-06-04
**Scope:** ZA.Templates `za-clean` — read path only; writes untouched. `za-vertical-slice` and `Customers` out of scope.
**Target version:** ZA.Templates v0.14.0 (minor — public-shape change in template, `IOrderRepository` signature change)
**Closes:** [#173](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/issues/173)
**Branch:** `feat/flat-read-model` off `main` at `6924124`

## Background

`GET /orders/{id}` currently rebuilds a full domain `Order` aggregate just to project it back to a flat JSON response:

1. `OrderRepository.GetByIdAsync` reads a head row + lines via ZA.ORM `[Query]`, then calls `Order.Materialize` — allocating `List<OrderLine>` of capacity N, N × `OrderLine` records, N × `Money` value objects, one `Money` for total, and the `Order` aggregate itself. `Enum.Parse<OrderStatus>(string)` for status and `MoneyConverter.FromStorage` (decimal-parse) for each monetary value.
2. `OrderToResponse.Map` then projects the domain into the wire format — allocating an `OrderLineResponse[]` of size N, N × `OrderLineResponse` records, and one `OrderResponse`.

Per read with N=2 lines: ~9 allocations + redundant `Enum.Parse` and `Money` construction, all to render a JSON shape that has no use for the domain aggregate.

The clean-arch template enforces (via NetArchTest) that Application has no dependency on Infra or Api, that Domain has no dependency on anything outside Domain, and that handlers live only in Application. **The current code is consistent with those rules but reflects a missing concept: a read-side query model, distinct from both the domain aggregate and the wire DTO.**

## Decision

Adopt **Approach A** from the brainstorm: introduce a sanctioned `OrderReadModel` in `MyApp.Application` as the query-side contract. The repo populates it directly. The wire DTO `OrderResponse` keeps its place in `MyApp.Api.Dtos`. A trivial flat-to-flat projector in Api converts read model to response.

Two records on the read path instead of "domain aggregate + wire DTO" — but each has a clear layer ownership. **Writes stay unchanged** — `Order.AddAsync` continues to use the full domain aggregate for invariants.

Rejected:
- **Approach B** (collapse read model with wire DTO, move `OrderResponse` to Application). Strictly cheaper allocations (2 + N vs 2 + 2N) but couples Application to the JSON wire format. For an educational template, the separation of "what queries return" vs "what the wire produces" is the lesson — collapsing them removes a clean-arch teaching moment for one allocation per request.
- **Approach C** (status quo, close #173). Leaves a measurable win on the table; the issue's stated concern is real even if the magnitude is small.

## What changes

**New: `OrderReadModel` + `OrderLineReadModel`**

`content/za-clean/src/MyApp.Application/Queries/GetOrderById/OrderReadModel.cs`:

```csharp
namespace MyApp.Application.Queries.GetOrderById;

public sealed record OrderReadModel(
    OrderId OrderId,
    CustomerId CustomerId,
    string Status,         // raw string from DB — no Enum.Parse on read
    decimal Total,         // pre-decoded via MoneyConverter at the boundary
    string Currency,
    IReadOnlyList<OrderLineReadModel> Lines);

public sealed record OrderLineReadModel(string Sku, int Quantity, decimal Price);
```

Domain typed IDs (`OrderId`, `CustomerId`) flow through. Status stays a raw string (the wire format wants a string anyway). Money is decomposed into `decimal Total` + `string Currency` at the boundary, so no `Money` value object is constructed per row.

**Note on namespace.** `MyApp.Application.Queries.GetOrderById` co-locates the query, handler, and read model — easier to navigate than a `MyApp.Application.QueryModels/` grab-bag. Symmetric with the existing `MyApp.Application.CreateOrder/` shape.

**Move `GetOrderByIdQuery` + `GetOrderByIdHandler`** to the new namespace if they aren't already. Currently they're at `MyApp.Application.GetOrderById/`. Decide during implementation Task 1: either move both into `MyApp.Application.Queries.GetOrderById/` (consistent with the new read model location) OR keep them at `MyApp.Application.GetOrderById/` and put the read model alongside them. **Prefer keeping at `MyApp.Application.GetOrderById/`** — fewer file moves, no test churn, the namespace already conveys "this is a query".

So the read model file is actually at: `content/za-clean/src/MyApp.Application/GetOrderById/OrderReadModel.cs`.

**Repository signature change**

`content/za-clean/src/MyApp.Application/IOrderRepository.cs`:

```diff
- Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);
+ Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct);
```

`content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs` — `GetByIdAsync` rewritten to populate `OrderReadModel` directly. The strategy:

1. Try ZA.ORM v1.6.0's composite-in-list emit by changing the `[Query]` return shape to `Task<OrderReadModel?>`. ORM v1.6.0 supports composite `InnerColumns` in list result sets, which is exactly what `OrderReadModel.Lines : IReadOnlyList<OrderLineReadModel>` requires. **Spike this in implementation Task 1** before committing to the approach.
2. If ORM v1.6.0 doesn't handle the typed-ID columns (`OrderId`, `CustomerId`) inside the read model cleanly, fall back: keep the existing `(OrderHeadRow, IReadOnlyList<OrderLineRow>)` shape from `[Query]`, but in the body of `GetByIdAsync`, project the rows directly into `OrderReadModel`/`OrderLineReadModel` records (skipping `Order.Materialize`). Same allocation profile, just one extra step.

**Handler return type change**

`content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdHandler.cs`:

```diff
-     : IRequestHandler<GetOrderByIdQuery, Result<Order, ApplicationError>>
+     : IRequestHandler<GetOrderByIdQuery, Result<OrderReadModel, ApplicationError>>
```

Body changes are trivial — repo returns `OrderReadModel?`, handler wraps it the same way.

**Rename + repurpose the mapper**

`content/za-clean/src/MyApp.Api/Mappings/OrderToResponse.cs` → `ReadModelToResponse.cs`:

```csharp
public static class ReadModelToResponse
{
    public static OrderResponse Map(OrderReadModel rm)
    {
        var lines = new OrderLineResponse[rm.Lines.Count];
        for (var i = 0; i < rm.Lines.Count; i++)
            lines[i] = new OrderLineResponse(rm.Lines[i].Sku, rm.Lines[i].Quantity, rm.Lines[i].Price);
        return new OrderResponse(
            OrderId: rm.OrderId,
            CustomerId: rm.CustomerId,
            Status: rm.Status,
            Total: rm.Total,
            Currency: rm.Currency,
            Lines: lines);
    }
}
```

Flat field copy, lines array projection. No Domain types referenced.

**Endpoint**

`content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs` GET handler:

```diff
-     ? Results.Ok(OrderToResponse.Map(result.Value))
+     ? Results.Ok(ReadModelToResponse.Map(result.Value))
```

One-token edit.

## Arch test addition

`content/za-clean/tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` gains:

```csharp
[Fact]
public void Query_handlers_return_application_query_models_not_domain_entities()
{
    // Enumerate every IRequestHandler<TQuery, TResult> in Application.
    // If TResult is Result<TInner, _> AND TInner is an entity type (not a value object) from
    // the Domain assembly, fail the test.
    //
    // Approach: walk Types.InAssembly(Application).That().ImplementInterface(IRequestHandler<,>),
    // unwrap their generic args, unwrap Result<T,E>, check T.Assembly. Value objects (which live
    // in Domain.ValueObjects) are exempt — those are the "primitives" of the Domain and crossing
    // layer boundaries is fine.
    //
    // Practical heuristic: T.Namespace ends in ".ValueObjects" -> allowed; otherwise if T is in
    // the Domain assembly -> fail.
}
```

This pins the new invariant cleanly. `CreateOrderHandler` returning `Result<OrderId, ApplicationError>` stays legal (`OrderId` lives in `MyApp.Domain.ValueObjects`). `GetOrderByIdHandler` previously returned `Result<Order, ...>` — that would now fail the rule, which is exactly the regression net we want.

## Versioning + release

- `feat(za-clean):` commit. `IOrderRepository` is a public-visible contract change in the template (user templates may already have started from earlier versions and reference `Order?` directly). Minor bump is correct.
- release-please cuts **v0.14.0** (minor — `feat:` → minor per the repo's release-please config; this is **not** the `perf:`-vs-`patch` trap that bit us on #172).
- Template auto-publishes to NuGet via release-please.yml (no separate pack-push).

## Allocation target

Measured via `ReadHotPathBench.ZeroAlloc_ORM` (the existing bench with N=2 lines):

| | Before | After |
|---|---:|---:|
| Allocations (count) | ~11 | ~6 |
| Per-line cost | 3 (OrderLine + Money + List node) | 2 (OrderLineReadModel + OrderLineResponse) |
| One-time | Order, Money(total), List<OrderLine>, OrderLineResponse[], OrderResponse, Enum.Parse | OrderReadModel, OrderLineReadModel[], OrderResponse, OrderLineResponse[] |
| Decode work eliminated | — | `Enum.Parse<OrderStatus>`, `Money(Amount, Currency)` per row |

Measurement is in BDN `[MemoryDiagnoser]`'s `Allocated` column. Acceptance: bench shows a material reduction (~40-50% drop expected for N=2).

## Tests + acceptance

- `GetOrderByIdHandlerTests` updated to assert `Result<OrderReadModel, _>` shape.
- `FakeOrderRepository.GetByIdAsync` returns `OrderReadModel?` directly.
- New unit test for `ReadModelToResponse.Map` (pure projector — three assertions: OrderId/CustomerId pass-through, status string pass-through, Lines array shape).
- New or extended `GetOrderByIdEndpointTests` integration test asserting the JSON wire shape didn't drift after the move.
- New arch test fact: `Query_handlers_return_application_query_models_not_domain_entities` passes.
- Existing `OrderRepositoryAtomicityTests`, write-path tests, etc. — unaffected, all green.
- `ReadHotPathBench` post-change run shows the documented allocation reduction.

## Out of scope

- **`Customers` entity** — same pattern; tackle separately if Orders proves out.
- **`za-vertical-slice`** — collapses layers; the read-model concept is implicit in the slice. No change.
- **Additional benchmarks** — `ReadHotPathBench` covers the measurement.
- **Streaming or paginated reads** — single-record query only.

## Risk

- **ORM v1.6.0 composite emit fit.** Verify in implementation Task 1 that `[Query]` can flatten into a record whose fields include `OrderId`/`CustomerId` typed-ID structs. If it can't, the fallback (manual projection in `GetByIdAsync`) is straightforward and has the same allocation profile.
- **Test surface shift.** Three test files need updates (handler unit, fake repo, possibly endpoint integration). Routine.
- **Arch test reflection.** The new `Query_handlers_return_application_query_models_not_domain_entities` test uses reflection on closed generic types (`IRequestHandler<TQuery, Result<T, _>>` → unwrap → check `T.Assembly`). Standard NetArchTest doesn't have a built-in for this — hand-rolled Reflection. Fine for a test assembly; not in published binaries.
- **Pedagogical clarity.** The template's README should add a paragraph explaining the asymmetry: "writes use the domain aggregate for invariants; reads use a query model for shape. Different jobs, different shapes." Without that, readers may assume the new `OrderReadModel` is a "smell" rather than the intended pattern.
