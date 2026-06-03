# `za-clean` Immutable Order Aggregate — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.Templates `content/za-clean/`, refactor
**Closes:** #166
**Branch:** `refactor/za-clean-immutable-order` off `main` at `6222fb0` (post-v0.13.0)

## Background

`Order.AssignPersistenceId(OrderId)` in `MyApp.Domain.Order` exists to stamp the DB-generated identity onto the aggregate after the repository's `INSERT … RETURNING Id`. The pattern is pragmatic but architecturally dishonest — it places a persistence concern in the Domain layer of a template that advertises NetArchTest-enforced Clean Architecture.

NetArchTest's existing rules enforce dependency direction (`Domain → no Infrastructure/Application/Api/EF/AspNet`), so `AssignPersistenceId` doesn't *break* any current rule. But it's a public mutator on a Domain aggregate that exists solely for repository use — exactly the kind of leak the boundary tries to keep out.

## Decision

Adopt Approach A from the brainstorm: **repository returns a new `Order` instance via `Order.Materialize(...)` rather than mutating the input.** Remove `AssignPersistenceId`. Make `Order.Id` truly immutable. The DB-generated identity stays.

## What changes

**Files modified (5):**

1. **`content/za-clean/src/MyApp.Domain/Order.cs`** — remove the leak:
   - Delete `AssignPersistenceId(OrderId id)` method + its XML doc block (~9 lines)
   - Delete the leading comment about "Setter is private — only AssignPersistenceId can mutate Id"
   - Change `public OrderId Id { get; private set; }` → `public OrderId Id { get; }`
   - Delete the parameterless `private Order() { }` constructor (dead post-EF-swap — was the EF Core materialization hook)
   - Keep the 2-arg `private Order(OrderId id, CustomerId customerId)` ctor, `Order.Create`, `AddLine`, `Cancel`, `Order.Materialize` (no changes)

2. **`content/za-clean/src/MyApp.Application/IOrderRepository.cs`** — return the persisted aggregate:
   ```csharp
   public interface IOrderRepository
   {
       Task<Order> AddAsync(Order order, CancellationToken ct);   // was Task
       Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);
       Task<int> CountAsync(CancellationToken ct);
   }
   ```

3. **`content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`** — materialize + return:
   - Change `AddAsync` return type from `Task` to `Task<Order>`
   - After the head + lines inserts succeed and before `tx.CommitAsync(ct)`, build and return a fresh Order:
     ```csharp
     var persisted = Order.Materialize(
         new OrderId(orderId),
         order.CustomerId,
         order.Status,
         order.Total,
         order.Lines);
     await tx.CommitAsync(ct).ConfigureAwait(false);
     return persisted;
     ```
   - Delete the `order.AssignPersistenceId(new OrderId(orderId));` line (method no longer exists)

4. **`content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderHandler.cs:40-41`** — use the returned instance:
   ```csharp
   var persisted = await repo.AddAsync(order, ct).ConfigureAwait(false);
   return Result<OrderId, ApplicationError>.Success(persisted.Id);
   ```

5. **`content/za-clean/src/MyApp.Api/SeedData.cs:22`** — explicit discard:
   ```csharp
   _ = await repo.AddAsync(order, ct).ConfigureAwait(false);
   ```

## What this does to the Domain shape

After this refactor, `Order` has **zero public mutators that exist for the repository's benefit**. The remaining public mutators are:
- `AddLine(string sku, int quantity, Money price)` — domain behaviour (composing the aggregate)
- `Cancel()` — domain behaviour (state transition)

Both are honest domain operations. The Domain layer becomes architecturally consistent with the Clean Architecture pitch the template advertises.

## What stays the same

- DB-generated identity (no schema change)
- `Order.Create`, `Order.Materialize`, `AddLine`, `Cancel`, the OrderId VO — all unchanged
- The `private Order(OrderId, CustomerId)` ctor (still used by `Create` + `Materialize`)
- All architecture tests pass without modification (they enforce dependency direction, which is already correct)

## Caller-side impact (small footgun)

Old pattern:
```csharp
var order = Order.Create(customerId);
await repo.AddAsync(order, ct);
return order.Id;     // mutated by AssignPersistenceId
```

New pattern:
```csharp
var order = Order.Create(customerId);
var persisted = await repo.AddAsync(order, ct);
return persisted.Id; // input order's Id is still the sentinel 0
```

Adopters who don't capture the return value but rely on `order.Id` will silently get the `OrderId(0)` sentinel. That's a **deliberate design choice**: forcing callers to think about which instance carries the assigned id is the architectural point. The XML doc on `AddAsync` should call this out explicitly.

## Tests

- **`CreateOrderEndpointTests`** — HTTP-level, doesn't touch the Order class directly, unaffected
- **`OrderRepositoryAtomicityTests`** (added in #162 close-out) — doesn't read `order.Id` after `AddAsync`, unaffected
- **All existing arch tests pass** — dependency direction unchanged
- **No new tests added** — the refactor reduces the public surface; the structural guarantee (no `AssignPersistenceId` to call from outside the Domain) is enforced by the type system

## Out of scope

- **Schema changes** (Approach B: client-generated IDs / GUIDs) — bigger overhaul, not warranted for a design-consistency note
- **vs (`za-vertical-slice`)** — vs's `Order` entity is documentation-shaped (never instantiated; PlaceOrder's flow doesn't go through the entity). No leak to fix
- **NetArchTest rule "no persistence-aware mutators"** — too brittle to express in NetArchTest; removing the mutator IS the architectural improvement
- **README updates** — the headline already states "Clean Architecture"; no false claim to scope back

## Commit shape

Single `refactor:` commit. release-please will cut this as **v0.13.1** (patch — refactor is not a semantic break for the template, though strictly it changes the `IOrderRepository` interface).

```
refactor(za-clean): immutable Order aggregate — remove persistence leak (closes #166)
```

(Squash title `refactor:` so release-please patch-bumps.)

## Acceptance criteria (from #166)

- [x] Decide: keep `AssignPersistenceId` (documented) or move identity assignment out of Domain. → **Move out.** Repository returns a new instance via `Order.Materialize()`.
- [x] If kept, ensure architecture tests still encode the intended boundary. → **N/A** (not kept). The existing dependency-direction rules continue to enforce the boundary structurally.
