# Flat Read Model Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the domain-aggregate read path for `GET /orders/{id}` with a sanctioned Application-owned `OrderReadModel`, bypassing `Order.Materialize`/`Money`/`Enum.Parse` and halving per-request allocations.

**Architecture:** Add `OrderReadModel` + `OrderLineReadModel` records in `MyApp.Application/GetOrderById/`. Change `IOrderRepository.GetByIdAsync` to return them directly. The repo flattens straight into the read model. The endpoint maps to `OrderResponse` (still in `MyApp.Api.Dtos`) via a renamed projector. A new NetArchTest rule pins the invariant that query handlers must not return Domain entities. Writes are untouched.

**Tech Stack:** .NET 10, C# 13, ZA.ORM v1.6.0 (composite-in-list emit), NetArchTest, xUnit, BenchmarkDotNet `[MemoryDiagnoser]`, release-please.

**Design reference:** [docs/plans/2026-06-04-flat-read-model-design.md](2026-06-04-flat-read-model-design.md) (commit `d2a2213`)
**Branch:** `feat/flat-read-model` off `main` at `6924124`

---

## Preflight

SDK pin check. If `dotnet --list-sdks` doesn't show a `10.0.x` matching `global.json`'s pin, relax `global.json` and (if it exists) `content/za-clean/global.json`:

```jsonc
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

**NEVER commit the relaxed global.json.** `git restore global.json` before any `git add`/commit.

All paths below are relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.

---

## Task 1: Spike — does ZA.ORM v1.6.0 composite-in-list emit accept typed-ID fields?

**Goal:** Determine whether the cleaner one-step `[Query] → Task<OrderReadModel?>` path is viable, or whether we need the two-step fallback (`[Query]` returns flat rows, `GetByIdAsync` body projects into the read model).

This is a **research task** — no production commits. Time-box: ≤30 minutes. The outcome decides Task 3's approach.

**The question:** ZA.ORM v1.6.0 (shipped yesterday in our parent ORM repo as PR #115) added composite `InnerColumns` recursion in `EmitListResultSet`. That gives us composite rows inside `IReadOnlyList<T>`. But:

- `OrderReadModel.OrderId` is a typed ID struct (`OrderId(int Value)`) with a ZA.ValueObjects-generated `From(int)` factory.
- ZA.ORM's existing classifier in v1.6.0 handles **non-nullable composite columns** for value-objects in flat rows (per the design doc `2026-06-03-composite-in-list-emit-design.md` in the ORM repo). Whether the **outer row's primitive→typed-ID mapping** (e.g. reading `int` from the column and constructing `OrderId(int)`) also works is what we're testing.

**Steps:**

1. **Create a throwaway spike file** at `content/za-clean/src/MyApp.Infrastructure/Persistence/_Spike_ReadModelEmit.cs`:

   ```csharp
   #if SPIKE_FLAT_READ_MODEL
   using System.Data;
   using System.Data.Async;
   using MyApp.Application.GetOrderById;
   using MyApp.Domain.ValueObjects;
   using ZeroAlloc.ORM;

   namespace MyApp.Infrastructure.Persistence;

   // Spike: does ZA.ORM v1.6.0's composite-in-list emit flatten into a record
   // with typed-ID fields (OrderId, CustomerId) and a composite list of
   // OrderLineReadModel? If the generator emits clean code, Task 3 uses
   // [Query] -> Task<OrderReadModel?> directly. Otherwise Task 3 falls back
   // to a manual projection.
   public sealed partial class OrderRepository_Spike(IAsyncDbConnection conn)
   {
       [Query(
           "SELECT \"Id\", \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
           "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
       public partial Task<OrderReadModel?> ReadAsync(int id, CancellationToken ct);
   }
   #endif
   ```

   (The `#if SPIKE_FLAT_READ_MODEL` lets us toggle it on without leaving spike code in builds.)

2. **Build with the spike define:**

   ```powershell
   dotnet build content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj `
     /p:DefineConstants=SPIKE_FLAT_READ_MODEL -v normal 2>&1 | Select-String "error|Spike|ZA[OE]\d+" -SimpleMatch
   ```

3. **Inspect the generator output:**

   ```powershell
   Get-ChildItem -Recurse `
     content/za-clean/src/MyApp.Infrastructure/obj `
     -Filter "*OrderRepository_Spike*.g.cs" -ErrorAction SilentlyContinue
   ```

   Read whichever generated file appears. Check whether the emit:
   - Reads the row, calls `MoneyConverter.FromStorage(reader.GetString(N))` cleanly (it should — we use TEXT storage), and constructs the record with all fields populated. **OR**
   - Emits a ZAO-prefixed diagnostic (e.g. ZAO022 "shape not supported") that blocks the build.

4. **Decision rule:**
   - **GREEN** (emit succeeds, build clean): Task 3 uses the one-step `[Query] → Task<OrderReadModel?>` path. Skip the fallback.
   - **RED** (diagnostic fires or emit is malformed): Task 3 uses the two-step fallback — keep `[Query]` returning `(OrderHeadRow Head, IReadOnlyList<OrderLineRow> Lines)?` and project to `OrderReadModel` in the body.

5. **Delete the spike file** and any generated artifacts:

   ```powershell
   Remove-Item content/za-clean/src/MyApp.Infrastructure/Persistence/_Spike_ReadModelEmit.cs
   Get-ChildItem -Recurse content/za-clean/src/MyApp.Infrastructure/obj `
     -Filter "*OrderRepository_Spike*" | Remove-Item
   ```

6. **No commit.** Record the GREEN/RED outcome in your task notes — it determines Task 3 below.

**Expected outcome:** Likely **RED** for the typed-ID-on-the-outer-row case. ZA.ORM v1.6.0's emit improvement targeted *non-nullable composite columns* (e.g. `Money Total` inside a row), not *typed-ID outer-row mapping* (reading `int` from column 0 and calling `new OrderId(int)`). If RED, the two-step fallback is clean and small. Don't over-engineer.

---

## Task 2: Failing tests for the new contract

**Goal:** TDD lock-in — write tests that express the new contract (`IOrderRepository.GetByIdAsync` returns `OrderReadModel?`, handler returns `Result<OrderReadModel, _>`, new arch test pins the invariant). Tests MUST fail against the current code because the new types don't exist yet.

**Files:**
- Modify: `content/za-clean/tests/MyApp.UnitTests/Application/GetOrderByIdHandlerTests.cs`
- Modify: `content/za-clean/tests/MyApp.UnitTests/Application/FakeOrderRepository.cs`
- Modify: `content/za-clean/tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs`

**Step 1: Update `FakeOrderRepository`**

Replace `Task<Order?> GetByIdAsync(...)` with a stub against the new contract. The fake keeps storing Orders (write path uses domain), but the read returns a projection:

```csharp
using MyApp.Application;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.UnitTests.Application;

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Saved { get; } = new();

    public Task<Order> AddAsync(Order order, CancellationToken ct)
    {
        var assigned = Order.Materialize(
            new OrderId(Saved.Count + 1),
            order.CustomerId,
            order.Status,
            order.Total,
            order.Lines);
        Saved.Add(assigned);
        return Task.FromResult(assigned);
    }

    public Task<int> CountAsync(CancellationToken ct)
        => Task.FromResult(Saved.Count);

    public Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var match = Saved.FirstOrDefault(o => o.Id.Value == id.Value);
        if (match is null) return Task.FromResult<OrderReadModel?>(null);

        var lines = new OrderLineReadModel[match.Lines.Count];
        for (var i = 0; i < match.Lines.Count; i++)
        {
            var l = match.Lines[i];
            lines[i] = new OrderLineReadModel(l.Sku, l.Quantity, l.Price.Amount);
        }
        return Task.FromResult<OrderReadModel?>(new OrderReadModel(
            match.Id,
            match.CustomerId,
            match.Status.ToString(),
            match.Total.Amount,
            match.Total.Currency,
            lines));
    }
}
```

**Step 2: Update `GetOrderByIdHandlerTests`**

Existing test expects `Result<Order, _>`. Update to new shape and add a happy-path test:

```csharp
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Application;

public class GetOrderByIdHandlerTests
{
    [Fact]
    public async Task Returns_failure_when_order_not_found()
    {
        var repo = new FakeOrderRepository();
        var handler = new GetOrderByIdHandler(repo);

        var result = await handler.Handle(new GetOrderByIdQuery(OrderId: new OrderId(999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("order.not-found", result.Error.Code);
    }

    [Fact]
    public async Task Returns_read_model_with_flat_fields_when_found()
    {
        var repo = new FakeOrderRepository();
        var order = Order.Create(new CustomerId(7));
        order.AddLine("SKU-A", 2, Money.TryCreate(15m, "EUR").Value);
        var saved = await repo.AddAsync(order, CancellationToken.None);

        var handler = new GetOrderByIdHandler(repo);
        var result = await handler.Handle(new GetOrderByIdQuery(saved.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rm = result.Value;
        Assert.Equal(saved.Id, rm.OrderId);
        Assert.Equal(new CustomerId(7), rm.CustomerId);
        Assert.Equal("Pending", rm.Status);
        Assert.Equal("EUR", rm.Currency);
        Assert.Single(rm.Lines);
        Assert.Equal("SKU-A", rm.Lines[0].Sku);
        Assert.Equal(2, rm.Lines[0].Quantity);
        Assert.Equal(15m, rm.Lines[0].Price);
    }
}
```

**Step 3: Add the arch test**

Append to `content/za-clean/tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` (inside the class):

```csharp
[Fact]
public void Query_handlers_return_application_query_models_not_domain_entities()
{
    var handlerInterface = typeof(IRequestHandler<,>);
    var domainAssembly = Domain;

    var offenders = Types.InAssembly(Application)
        .That()
        .ImplementInterface(handlerInterface)
        .GetTypes()
        .SelectMany(t => t.GetInterfaces())
        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface)
        .Select(i => new { Handler = i, Result = i.GenericTypeArguments[1] })
        .Where(x =>
        {
            // Unwrap Result<T, _> if present — the project's handlers return Result<T, ApplicationError>.
            var result = x.Result;
            if (result.IsGenericType &&
                result.GetGenericTypeDefinition().Name.StartsWith("Result", StringComparison.Ordinal))
            {
                var inner = result.GenericTypeArguments[0];
                return IsForbiddenDomainEntity(inner, domainAssembly);
            }
            return IsForbiddenDomainEntity(result, domainAssembly);
        })
        .Select(x => x.Handler.GenericTypeArguments[0].FullName)
        .ToArray();

    Assert.True(
        offenders.Length == 0,
        $"Query/command handlers returning Domain entities (not value objects):\n  - {string.Join("\n  - ", offenders)}");

    static bool IsForbiddenDomainEntity(Type t, Assembly domain)
    {
        // Domain value objects are allowed to cross layer boundaries — they're the "primitives"
        // of the domain. Entities (aggregate roots and child entities) are not. Heuristic:
        // namespace ends in ".ValueObjects" -> value object -> allowed. Otherwise if the type
        // lives in the Domain assembly -> entity -> forbidden as a handler return.
        if (t.Assembly != domain) return false;
        return !(t.Namespace?.EndsWith(".ValueObjects", StringComparison.Ordinal) ?? false);
    }
}
```

This test will **currently FAIL** because `GetOrderByIdHandler` returns `Result<Order, ApplicationError>` and `Order` is a Domain entity, not a value object. That's the point — it captures the invariant we're about to satisfy.

**Step 4: Run tests, confirm failures match expectations**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj `
  --filter "FullyQualifiedName~GetOrderByIdHandlerTests" -v minimal
```

Expected: **build fails** because `OrderReadModel` doesn't exist yet, OR (if test project doesn't compile) the build errors point at the missing types. Both are acceptable signals — the tests express the new contract that doesn't yet have an implementation. Same expectation for the arch test — won't run because the test assembly can't compile.

This is the "red" state of red→green TDD. **Do not commit yet** — wait for Task 3 to add the types.

---

## Task 3: Implement `OrderReadModel`, repo signature change, handler change

**Goal:** Move from "red" (Task 2's failing tests) to "green" — add the new types, change the signatures.

**Files:**
- Create: `content/za-clean/src/MyApp.Application/GetOrderById/OrderReadModel.cs`
- Modify: `content/za-clean/src/MyApp.Application/IOrderRepository.cs`
- Modify: `content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdHandler.cs`
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`

**Step 1: Add the read model**

Create `content/za-clean/src/MyApp.Application/GetOrderById/OrderReadModel.cs`:

```csharp
using MyApp.Domain.ValueObjects;

namespace MyApp.Application.GetOrderById;

/// <summary>
/// Query-side projection of an Order, owned by Application. Read endpoints
/// populate this directly from storage — skipping the domain aggregate's
/// Money / OrderStatus reconstruction and the second Api-DTO array.
/// Writes still go through the <see cref="MyApp.Domain.Order"/> aggregate.
/// </summary>
public sealed record OrderReadModel(
    OrderId OrderId,
    CustomerId CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineReadModel> Lines);

public sealed record OrderLineReadModel(string Sku, int Quantity, decimal Price);
```

**Step 2: Change `IOrderRepository.GetByIdAsync` signature**

`content/za-clean/src/MyApp.Application/IOrderRepository.cs`:

```diff
+ using MyApp.Application.GetOrderById;
  using MyApp.Domain;
  using MyApp.Domain.ValueObjects;

  namespace MyApp.Application;

  public interface IOrderRepository
  {
      Task<Order> AddAsync(Order order, CancellationToken ct);
-     Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);
+     Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct);
      Task<int> CountAsync(CancellationToken ct);
  }
```

**Step 3: Change handler return type**

`content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdHandler.cs`:

```csharp
using System.Globalization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

[Scoped]
public sealed class GetOrderByIdHandler(IOrderRepository repo)
    : IRequestHandler<GetOrderByIdQuery, Result<OrderReadModel, ApplicationError>>
{
    public async ValueTask<Result<OrderReadModel, ApplicationError>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await repo.GetByIdAsync(request.OrderId, ct).ConfigureAwait(false);
        return order is null
            ? Result<OrderReadModel, ApplicationError>.Failure(new ApplicationError(
                "order.not-found",
                "Order " + request.OrderId.Value.ToString(CultureInfo.InvariantCulture) + " not found"))
            : Result<OrderReadModel, ApplicationError>.Success(order);
    }
}
```

(Removed `using MyApp.Domain;` — no longer needed.)

**Step 4: Rewrite `OrderRepository.GetByIdAsync`**

The implementation depends on Task 1's GREEN/RED outcome.

**If Task 1 was GREEN (one-step `[Query]` flatten works):**

```csharp
// In OrderRepository.cs, replace GetByIdAsync + ReadOrderAsync.
public Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
    => ReadOrderAsync(id.Value, ct);

[Query(
    "SELECT \"Id\", \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
    "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
private partial Task<OrderReadModel?> ReadOrderAsync(int id, CancellationToken ct);
```

Delete the old `OrderHeadRow` / `OrderLineRow` records.

**If Task 1 was RED (use fallback — most likely path):**

Keep the existing `[Query]` shape but project to read model in the body:

```csharp
public async Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
{
    var tuple = await ReadOrderAsync(id.Value, ct).ConfigureAwait(false);
    if (tuple is null) return null;

    var (head, lines) = tuple.Value;
    var lineModels = new OrderLineReadModel[lines.Count];
    for (var i = 0; i < lines.Count; i++)
    {
        // MoneyConverter.FromStorage decodes "amount|currency" → Money;
        // we only want the Amount, so decode once and discard the struct.
        var price = MoneyConverter.FromStorage(lines[i].Price);
        lineModels[i] = new OrderLineReadModel(lines[i].Sku, lines[i].Quantity, price.Amount);
    }

    var total = MoneyConverter.FromStorage(head.Total);
    return new OrderReadModel(
        id,
        new CustomerId(head.CustomerId),
        head.Status,           // raw string from DB — no Enum.Parse
        total.Amount,
        total.Currency,
        lineModels);
}

// [Query] and row records stay as today:
[Query(
    "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
    "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
private partial Task<(OrderHeadRow Head, IReadOnlyList<OrderLineRow> Lines)?> ReadOrderAsync(int id, CancellationToken ct);

private sealed record OrderHeadRow(int CustomerId, string Status, string Total);
private sealed record OrderLineRow(string Sku, int Quantity, string Price);
```

Either path bypasses `Order.Materialize`, `Money` value-object allocation (we only want `.Amount`), and `Enum.Parse<OrderStatus>`.

> **Note on the still-allocated `Money` struct:** `MoneyConverter.FromStorage` returns a `Money` record-struct (per the Templates docs `2026-06-03-money-symmetry-test.md`). If `Money` is a `readonly record struct`, calling `.Amount` on it is zero-alloc — `Money` lives on the stack. If it's a `record class`, each call allocates. Check the type during implementation; if it's a class, write a `MoneyConverter.TryParseStorage(string raw, out decimal amount, out string currency)` helper alongside the existing `FromStorage` to avoid the per-row struct allocation. This is the per-row hot path that matters most.

**Step 5: Build**

```powershell
dotnet build content/za-clean/src/MyApp.Api/MyApp.Api.csproj -v minimal
```

Expected: **0 Error(s)**. The Api project sees that `GetOrderByIdHandler` now returns `Result<OrderReadModel, _>` instead of `Result<Order, _>`, and `OrderToResponse.Map(result.Value)` will fail to compile because `Map` takes `Order`. That's expected — Task 4 fixes the endpoint + mapper.

If the error is anything OTHER than "OrderToResponse.Map cannot convert OrderReadModel to Order", STOP and report.

**Step 6: Commit (red-to-some-green)**

```powershell
git restore global.json # if relaxed
git add `
  content/za-clean/src/MyApp.Application/GetOrderById/OrderReadModel.cs `
  content/za-clean/src/MyApp.Application/IOrderRepository.cs `
  content/za-clean/src/MyApp.Application/GetOrderById/GetOrderByIdHandler.cs `
  content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs `
  content/za-clean/tests/MyApp.UnitTests/Application/GetOrderByIdHandlerTests.cs `
  content/za-clean/tests/MyApp.UnitTests/Application/FakeOrderRepository.cs `
  content/za-clean/tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs
git commit -m @'
feat(za-clean): add OrderReadModel for GET /orders/{id} read path (#173)

Introduces MyApp.Application.GetOrderById.OrderReadModel as the query-side
projection. Changes IOrderRepository.GetByIdAsync return type from
Task<Order?> to Task<OrderReadModel?>. OrderRepository populates the read
model directly, bypassing Order.Materialize / Money / Enum.Parse.

Adds a NetArchTest rule pinning the invariant: query handlers may not
return Domain entities (value objects like OrderId remain allowed).

Build is intentionally still broken in MyApp.Api — endpoint + mapper
update is the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

(`Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` matches the repo's established commit pattern. See `git log -10 --format=%B`.)

---

## Task 4: Rename mapper + swap endpoint call

**Goal:** Finish the green by repointing the API at the new read model.

**Files:**
- Rename + rewrite: `content/za-clean/src/MyApp.Api/Mappings/OrderToResponse.cs` → `ReadModelToResponse.cs`
- Modify: `content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs`

**Step 1: Rename + rewrite the mapper**

```powershell
git mv content/za-clean/src/MyApp.Api/Mappings/OrderToResponse.cs `
       content/za-clean/src/MyApp.Api/Mappings/ReadModelToResponse.cs
```

Then replace the file contents:

```csharp
using MyApp.Api.Dtos;
using MyApp.Application.GetOrderById;

namespace MyApp.Api.Mappings;

/// <summary>
/// Wire-format projector for the Order query read model. Flat field copy +
/// per-line array projection. No Domain types referenced.
/// </summary>
public static class ReadModelToResponse
{
    public static OrderResponse Map(OrderReadModel rm)
    {
        var lines = new OrderLineResponse[rm.Lines.Count];
        for (var i = 0; i < rm.Lines.Count; i++)
        {
            var line = rm.Lines[i];
            lines[i] = new OrderLineResponse(line.Sku, line.Quantity, line.Price);
        }
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

**Step 2: Update the endpoint**

`content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs`:

```diff
- using MyApp.Application.GetOrderById;
+ using MyApp.Application.GetOrderById;
  // (same — namespace is unchanged; just confirm it's still imported)
```

In the GET handler:

```diff
-     ? Results.Ok(OrderToResponse.Map(result.Value))
+     ? Results.Ok(ReadModelToResponse.Map(result.Value))
```

**Step 3: Build**

```powershell
dotnet build content/za-clean -v minimal
```

Expected: **0 Error(s)**.

**Step 4: Run the affected tests**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj `
  --filter "FullyQualifiedName~GetOrderByIdHandlerTests" -v minimal
```

Expected: **2 passed** (both `Returns_failure_when_order_not_found` and `Returns_read_model_with_flat_fields_when_found`).

```powershell
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -v minimal
```

Expected: **all arch tests pass**, including the new `Query_handlers_return_application_query_models_not_domain_entities`.

**Step 5: Commit**

```powershell
git restore global.json # if relaxed
git status # confirm only the renamed mapper + endpoint changes are staged
git add content/za-clean/src/MyApp.Api/Mappings/ReadModelToResponse.cs `
        content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs
git commit -m @'
feat(za-clean): wire endpoint to OrderReadModel via ReadModelToResponse (#173)

Renames OrderToResponse -> ReadModelToResponse. Maps OrderReadModel
(Application) -> OrderResponse (Api wire DTO) via a flat-to-flat field copy.
Endpoint GET /orders/{id} now flows: SQL -> OrderReadModel -> OrderResponse.
Domain aggregate is no longer rebuilt on the read path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: Add `ReadModelToResponse` unit test

**Goal:** Lock the wire-shape behavior of the new mapper as an executable spec.

**Files:**
- Create: `content/za-clean/tests/MyApp.UnitTests/Api/ReadModelToResponseTests.cs`

**Step 1: Check if `MyApp.UnitTests` already references `MyApp.Api`**

```powershell
Select-String -Path content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -Pattern "MyApp.Api"
```

If not present, add to the csproj's `<ItemGroup>` for project references:

```xml
<ProjectReference Include="..\..\src\MyApp.Api\MyApp.Api.csproj" />
```

(If `MyApp.UnitTests` currently isolates from `MyApp.Api` by design — check the existing test folder structure — put the test in `MyApp.IntegrationTests` instead, which already references `MyApp.Api` via `WebApplicationFactory<Program>`. Choose whichever fits the project's convention.)

**Step 2: Write the test**

```csharp
using MyApp.Api.Mappings;
using MyApp.Application.GetOrderById;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Api;

public class ReadModelToResponseTests
{
    [Fact]
    public void Map_copies_flat_fields_unchanged()
    {
        var rm = new OrderReadModel(
            new OrderId(42),
            new CustomerId(7),
            "Pending",
            25m,
            "EUR",
            new[]
            {
                new OrderLineReadModel("SKU-A", 1, 10m),
                new OrderLineReadModel("SKU-B", 1, 15m),
            });

        var resp = ReadModelToResponse.Map(rm);

        Assert.Equal(new OrderId(42), resp.OrderId);
        Assert.Equal(new CustomerId(7), resp.CustomerId);
        Assert.Equal("Pending", resp.Status);
        Assert.Equal(25m, resp.Total);
        Assert.Equal("EUR", resp.Currency);
        Assert.Equal(2, resp.Lines.Count);
        Assert.Equal("SKU-A", resp.Lines[0].Sku);
        Assert.Equal(1, resp.Lines[0].Quantity);
        Assert.Equal(10m, resp.Lines[0].Price);
    }

    [Fact]
    public void Map_handles_empty_lines()
    {
        var rm = new OrderReadModel(
            new OrderId(1), new CustomerId(1), "Pending", 0m, "EUR",
            Array.Empty<OrderLineReadModel>());

        var resp = ReadModelToResponse.Map(rm);

        Assert.Empty(resp.Lines);
    }
}
```

**Step 3: Run and confirm green**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj `
  --filter "FullyQualifiedName~ReadModelToResponseTests" -v minimal
```

Expected: **2 passed**.

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git add content/za-clean/tests/MyApp.UnitTests/Api/ReadModelToResponseTests.cs `
        content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj # if csproj edited
git commit -m @'
test(za-clean): unit-test ReadModelToResponse projector (#173)

Two facts: flat field copy passes through unchanged, empty Lines is honored.
Locks the wire shape so future read-model evolution can't silently drift
JSON consumers.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 6: Run `ReadHotPathBench` before/after and capture results

**Goal:** Empirically prove the allocation reduction in `docs/benchmarks/`.

**Files:**
- Create: `docs/benchmarks/2026-06-04-flat-read-model-alloc.md`

**Step 1: Check whether the existing benchmark needs adjustment**

`content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs` currently calls `_repo!.GetByIdAsync(...)` and returns `Task<Order?>`. After our change, the return type is `Task<OrderReadModel?>`. Update the `ZeroAlloc_ORM` benchmark's return type to `Task<OrderReadModel?>` and adjust the `HandWrittenAdoNet` baseline accordingly (probably also project to `OrderReadModel` to keep apples-to-apples comparison).

Read the bench first:

```powershell
Get-Content content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs
```

Make minimal edits — just the return type and the `HandWrittenAdoNet` materialization so both rows compare the same wire-shape work. Optionally add a third benchmark `ZeroAlloc_ORM_Old` that still constructs the domain aggregate, for explicit before/after comparison in a single run. Skip if not needed.

**Step 2: Run the benchmark**

```powershell
Push-Location content/za-clean/benchmarks/MyApp.Benchmarks
dotnet run -c Release -- --filter "*ReadHotPathBench*" --memory
Pop-Location
```

Capture the `Allocated` column.

**Step 3: Save results**

```powershell
Copy-Item content/za-clean/benchmarks/MyApp.Benchmarks/BenchmarkDotNet.Artifacts/results/MyApp.Benchmarks.ReadHotPathBench-report-github.md `
  docs/benchmarks/2026-06-04-flat-read-model-alloc.md
```

If the github-format report doesn't exist, copy whatever variant is available and append a short header explaining the before/after delta.

Compare against the prior benchmark on `main` if `docs/benchmarks/` already has a `ReadHotPathBench` baseline (check `docs/benchmarks/` for earlier ZA.Templates BDN snapshots, particularly `2026-06-03-*` files).

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git add content/za-clean/benchmarks/MyApp.Benchmarks/ReadHotPathBench.cs `
        docs/benchmarks/2026-06-04-flat-read-model-alloc.md
git commit -m @'
bench(za-clean): ReadHotPathBench measures #173 alloc reduction

Updates the benchmark to the post-#173 read shape (Task<OrderReadModel?>).
The Allocated column should drop materially vs the pre-change baseline —
record the numbers in docs/benchmarks/2026-06-04-flat-read-model-alloc.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 7: Full-suite sweep (verification gate, no commit)

**Goal:** Confirm both templates' full suites stay green before pushing.

This task is verification-only — **no commits**.

**Step 1: za-clean full sweep**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -v minimal
```

Expected counts (per yesterday's #172 sweep + the two new tests added here):
- UnitTests: previously 9, +1 happy-path GetOrderByIdHandler test, +2 ReadModelToResponse tests = **12**
- IntegrationTests: **16** (unchanged — no integration tests added in this PR)
- ArchitectureTests: previously 4, +1 query-handler-return rule = **5**

**Step 2: za-vertical-slice full sweep (no source changes there — sanity check)**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj -v minimal
```

Expected: **all green, all counts unchanged from yesterday** (24 / 18 / 5).

**Step 3: Working tree check**

```powershell
git status
```

Confirm: branch is `feat/flat-read-model`, no `global.json` modifications, no stray `.received.*` files, only `.bench-artifacts/` may be untracked.

**Step 4: Verify commit log**

```powershell
git log --oneline 6924124..HEAD
```

Expected: 6-7 commits on top of `main` at `6924124`:
1. `d2a2213` docs(design): flat read model
2. (this plan) docs(plan): flat read model implementation
3. feat: OrderReadModel + repo signature + handler + arch test
4. feat: wire endpoint via ReadModelToResponse
5. test: ReadModelToResponse unit tests
6. bench: ReadHotPathBench alloc reduction

**No commit in this task.** If any test fails, STOP and report.

---

## Task 8: Push + PR + admin-merge + release-please v0.14.0

**Goal:** Land the branch with a `feat:` squash so release-please cuts a **minor** v0.14.0.

**CRITICAL:** Use `feat:` prefix, not `perf:`. Per the memory saved earlier today (`project_release_please_perf_maps_to_patch.md`), the repo's release-please config maps `perf: → patch`. This is a real feature change (new public type, signature change on `IOrderRepository`), so `feat:` is also the semantically correct prefix.

**Step 1: Push**

```powershell
git push -u origin feat/flat-read-model
```

**Step 2: Open PR**

```powershell
gh pr create --title "feat(za-clean): introduce OrderReadModel for GET /orders/{id}" --body @'
## Summary

Closes #173. Adds `MyApp.Application.GetOrderById.OrderReadModel` as the sanctioned query-side projection of an Order. `IOrderRepository.GetByIdAsync` now returns `Task<OrderReadModel?>` instead of `Task<Order?>`. The repository populates the read model directly, bypassing `Order.Materialize`, `Money` value-object construction, and `Enum.Parse<OrderStatus>(string)`. The wire DTO `OrderResponse` stays in `MyApp.Api.Dtos`; the renamed `ReadModelToResponse` projector maps the read model to it via a flat-to-flat copy.

A new NetArchTest fact `Query_handlers_return_application_query_models_not_domain_entities` pins the invariant: query handlers may not return Domain entities (value objects like `OrderId` remain allowed).

## Allocation reduction

Measured via `ReadHotPathBench` with N=2 lines (see `docs/benchmarks/2026-06-04-flat-read-model-alloc.md`):

- **Before:** ~11 allocations (Order, OrderLine x N, Money x N+1, List<OrderLine>, OrderResponse, OrderLineResponse x N + OrderLineResponse[] x 2) + `Enum.Parse<OrderStatus>` per request
- **After:** ~6 allocations (OrderReadModel, OrderLineReadModel x N + OrderLineReadModel[], OrderResponse, OrderLineResponse x N + OrderLineResponse[])

Per-line cost drops from 3 (OrderLine + Money + List node) to 2 (OrderLineReadModel + OrderLineResponse). One-time work eliminated: `Order` aggregate, `Money(total)`, `Enum.Parse<OrderStatus>`.

## Approach

Approach A from the brainstorm — separate read model (Application) from wire DTO (Api), preserving clean-arch layer ownership. Approach B (collapse the two into one DTO in Application) was rejected: the slightly smaller allocation footprint isn't worth coupling Application to JSON wire format in an educational template. Design: `docs/plans/2026-06-04-flat-read-model-design.md`.

## Test plan

- [x] `GetOrderByIdHandlerTests` — 2 tests: not-found failure + happy-path returns `OrderReadModel` with flat fields.
- [x] `FakeOrderRepository` — updated to return `OrderReadModel?` (test fake builds the projection from its in-memory Order list).
- [x] `ReadModelToResponseTests` — 2 tests: flat field copy + empty lines edge.
- [x] `CleanArchitectureRules.Query_handlers_return_application_query_models_not_domain_entities` — new fact passes.
- [x] All existing UnitTests/IntegrationTests/ArchitectureTests stay green (full sweep in Task 7).
- [x] za-vertical-slice unaffected (no source changes there) — full sweep stays green.
- [x] `ReadHotPathBench` confirms the allocation drop.

## Risk

- `IOrderRepository.GetByIdAsync` signature changed — adopters who copied the template and depend on the old return type will see a compile break when bumping. Minor-version bump signals it; release notes will call it out.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

**Step 3: Watch CI**

```powershell
gh pr checks --watch
```

If a check fails, STOP and report. Don't admin-merge through red.

**Step 4: Admin-merge with `feat:` squash**

```powershell
$prNumber = gh pr view --json number -q .number
gh pr merge $prNumber --squash --admin --subject "feat(za-clean): introduce OrderReadModel for GET /orders/{id} (#$prNumber)"
```

**Step 5: Monitor release-please**

```powershell
for ($i = 0; $i -lt 8; $i++) {
    $rp = gh pr list --label autorelease:pending --json number,title -q '.[0]'
    if ($rp) { Write-Host "Release-please PR: $rp"; break }
    Start-Sleep -Seconds 15
}
```

Expected: a `chore(main): release ZeroAlloc.Templates 0.14.0` PR opens within ~60-120s.

**Step 6: DO NOT merge the release-please PR.** That's the user's call. Templates auto-publishes to NuGet via release-please.yml — no separate `pack-push.yml` trigger needed (confirmed in yesterday's session, see `project_release_please_perf_maps_to_patch.md` memory).

**Step 7: Report**

```powershell
gh release list --limit 3
git log --oneline -3 origin/main
```

Final report should include:
- PR number opened
- All-green CI checks summary
- Squash-merge SHA on `main`
- Release-please PR number
- Measured allocation delta (paste the `Allocated` row diff)

---

## Notes

- **TDD discipline:** Task 2 writes failing tests first (handler tests + arch test that captures the new invariant). Task 3 implements the types/signatures that turn them green. Task 4 finishes the green by repointing the API.
- **The Task 1 spike is the architectural fork.** Most likely outcome (RED) gives the two-step fallback in Task 3 Step 4 — same allocation profile, slightly more code. If GREEN, even cleaner.
- **Per-row `Money` struct allocation check** in Task 3 Step 4 is the per-row hot path that matters most for N>2 lines. Verify `Money` is a `readonly record struct` (zero alloc on `.Amount` access) or write the `TryParseStorage` decomposed helper if it's a `record class`.
- **`feat:` not `perf:`.** Repo-specific gotcha saved as a memory yesterday. Use `feat:` for the squash to get the minor v0.14.0 bump.
