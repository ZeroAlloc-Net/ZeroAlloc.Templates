# `za-vertical-slice` Money VO + Converter Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Adopt za-clean's `Money` VO + `MoneyConverter` pattern internally in `za-vertical-slice` so both providers store money uniformly as `TEXT` via the `"<amount>|<currency>"` wire shape, closing the `numeric`-on-Postgres vs `TEXT`-on-Sqlite divergence.

**Architecture:** Port two files (Money VO into `Common/`, MoneyConverter into `Persistence/`), flip the Postgres migration column type, and rewire the three Orders slices (PlaceOrder/GetOrder/ListOrders) to convert `decimal ↔ Money` at the persistence boundary. **Public wire shape (`{customerId, total}`) is preserved** — clients still send/receive bare `decimal total`; Money is an internal storage type. Currency hardcoded to `"EUR"` at the conversion site (vs has no multi-currency surface; matches za-clean's `unitPriceEur` convention).

**Tech Stack:** .NET 10 / ZeroAlloc.ORM 1.2.0 / ZeroAlloc.ValueObjects / xUnit / Sqlite (in-memory integration) / `IAsyncDbConnection` from `AdoNet.Async.Adapters`.

**Reference design doc:** `docs/plans/2026-06-03-vs-money-converter-design.md` (committed `ea0c9ee` on this branch).

**Working branch:** `chore/vs-money-converter` (already created off `main` at `11dea8d`).

> **Open-question resolution (was in design):** vs's existing PlaceOrder endpoint tests send `{customerId: 42, total: 99.99m}` and GetOrder tests assert `dto.Total == 12.34m`. **The wire shape is bare-decimal and stays bare-decimal.** Money is internal-only — handlers wrap incoming `decimal` into `Money(amount, "EUR")` before persistence and unwrap stored `Money.Amount` back to `decimal` on read. This is the smallest diff that closes the storage divergence without breaking any existing client / test payload.

> **Local SDK pin gotcha** (same as ZA.ORM): `global.json` pins SDK `10.0.300 latestMinor`; dev machine has 10.0.204 max. Before any `dotnet build`/`dotnet test`:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before `git commit`. **Never commit the relaxed pin.**

---

### Task 1: Port `Money` value object into `Common/`

**Files:**
- Create: `content/za-vertical-slice/src/MyApp/Common/Money.cs`
- Create: `content/za-vertical-slice/tests/MyApp.UnitTests/Common/MoneyTests.cs`

**Step 1: Write the failing tests first**

Create `content/za-vertical-slice/tests/MyApp.UnitTests/Common/MoneyTests.cs`:

```csharp
using MyApp.Common;
using Xunit;

namespace MyApp.UnitTests.Common;

public class MoneyTests
{
    [Fact]
    public void Money_rejects_negative_amount()
    {
        var result = Money.TryCreate(-1.00m, "EUR");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_rejects_empty_currency()
    {
        var result = Money.TryCreate(1.00m, "");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_accepts_valid_amount()
    {
        var result = Money.TryCreate(10.00m, "EUR");
        Assert.True(result.IsSuccess);
        Assert.Equal(10.00m, result.Value.Amount);
        Assert.Equal("EUR", result.Value.Currency);
    }

    [Fact]
    public void Money_accepts_zero_amount()
    {
        // za-clean's semantics: non-negative (>= 0). vs follows suit so the
        // two templates stay aligned. Strict-positive is enforced at the API
        // boundary via [GreaterThan(0)] on PlaceOrderCommand.Total.
        var result = Money.TryCreate(0m, "EUR");
        Assert.True(result.IsSuccess);
    }
}
```

**Step 2: Run to confirm fail (CS0246: type Money not found)**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter "FullyQualifiedName~MoneyTests"
```

Expected: build failure — `MyApp.Common.Money` doesn't exist yet.

**Step 3: Port the VO**

Create `content/za-vertical-slice/src/MyApp/Common/Money.cs`:

```csharp
using ZeroAlloc.Results;
using ZeroAlloc.ValueObjects;

namespace MyApp.Common;

[ValueObject]
public readonly partial struct Money
{
    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money, string> TryCreate(decimal amount, string currency)
    {
        if (amount < 0)
        {
            return Result<Money, string>.Failure("Amount must be non-negative");
        }

        if (string.IsNullOrEmpty(currency))
        {
            return Result<Money, string>.Failure("Currency required");
        }

        return Result<Money, string>.Success(new Money(amount, currency));
    }
}
```

(Port of `content/za-clean/src/MyApp.Domain/ValueObjects/Money.cs` — only the namespace differs: `MyApp.Common` vs `MyApp.Domain.ValueObjects`.)

**Step 4: Run tests to confirm pass**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter "FullyQualifiedName~MoneyTests"
```

Expected: **4/4 passed**.

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add content/za-vertical-slice/src/MyApp/Common/Money.cs content/za-vertical-slice/tests/MyApp.UnitTests/Common/MoneyTests.cs
git commit -m "feat(vs): port Money value object to Common/ (closes #163 part 1)

Port of za-clean's Money VO. [ValueObject]-attributed readonly struct
with Amount (decimal) + Currency (string), TryCreate factory rejecting
negative amounts and empty currency. Namespace MyApp.Common matches
vs's existing typed-ID location (CustomerId / OrderId).

4 unit tests cover: negative amount rejected, empty currency rejected,
valid amount accepted, zero accepted (non-negative semantics — same
as za-clean)."
```

---

### Task 2: Port `MoneyConverter` into `Persistence/`

**Files:**
- Create: `content/za-vertical-slice/src/MyApp/Persistence/MoneyConverter.cs`
- Create: `content/za-vertical-slice/tests/MyApp.UnitTests/Persistence/MoneyConverterTests.cs`

**Step 1: Write the failing tests first**

Create `content/za-vertical-slice/tests/MyApp.UnitTests/Persistence/MoneyConverterTests.cs`:

```csharp
using MyApp.Common;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Persistence;

public class MoneyConverterTests
{
    [Fact]
    public void RoundTrip_preserves_amount_and_currency()
    {
        var original = Money.TryCreate(99.99m, "EUR").Value;
        var stored = MoneyConverter.ToStorage(original);
        var restored = MoneyConverter.FromStorage(stored);

        Assert.Equal(original.Amount, restored.Amount);
        Assert.Equal(original.Currency, restored.Currency);
    }

    [Fact]
    public void RoundTrip_preserves_fractional_pennies()
    {
        // Four-decimal precision survives string roundtrip (no IEEE float lossiness).
        var original = Money.TryCreate(0.0001m, "USD").Value;
        var stored = MoneyConverter.ToStorage(original);
        var restored = MoneyConverter.FromStorage(stored);

        Assert.Equal(0.0001m, restored.Amount);
    }

    [Fact]
    public void ToStorage_uses_invariant_culture()
    {
        // Stored form must be culture-independent (no locale-dependent decimal
        // separators) so production data is portable across machine cultures.
        var m = Money.TryCreate(1234.56m, "EUR").Value;
        var stored = MoneyConverter.ToStorage(m);
        Assert.Equal("1234.56|EUR", stored);
    }

    [Fact]
    public void FromStorage_empty_returns_zero_default()
    {
        var restored = MoneyConverter.FromStorage("");
        Assert.Equal(0m, restored.Amount);
        Assert.Equal("USD", restored.Currency);
    }

    [Fact]
    public void FromStorage_malformed_returns_zero_default()
    {
        // No pipe separator — defensive fallback per the converter's contract.
        var restored = MoneyConverter.FromStorage("not-a-money-value");
        Assert.Equal(0m, restored.Amount);
        Assert.Equal("USD", restored.Currency);
    }
}
```

**Step 2: Run to confirm fail (CS0246: MoneyConverter not found)**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter "FullyQualifiedName~MoneyConverterTests"
```

Expected: build failure — `MyApp.Persistence.MoneyConverter` doesn't exist.

**Step 3: Port the converter**

Create `content/za-vertical-slice/src/MyApp/Persistence/MoneyConverter.cs`:

```csharp
using System.Globalization;
using MyApp.Common;

namespace MyApp.Persistence;

/// <summary>
/// Round-trip helpers for the <see cref="Money"/> value object's TEXT storage
/// format <c>"&lt;amount&gt;|&lt;currency&gt;"</c>. Used by the ZA.ORM-emitted
/// read/write paths in the Orders slices.
/// </summary>
public static class MoneyConverter
{
    public static string ToStorage(Money m)
        => m.Amount.ToString(CultureInfo.InvariantCulture) + "|" + m.Currency;

    public static Money FromStorage(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var pipe = s.IndexOf('|');
        if (pipe < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var amountSpan = s.AsSpan(0, pipe);
        var currency = s[(pipe + 1)..];
        if (amountSpan.IsEmpty || string.IsNullOrEmpty(currency)
            || !decimal.TryParse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        return Money.TryCreate(amount, currency).Value;
    }
}
```

(Port of `content/za-clean/src/MyApp.Infrastructure/Persistence/MoneyConverter.cs` — only the `using` namespace differs.)

**Step 4: Run tests to confirm pass**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj --filter "FullyQualifiedName~MoneyConverterTests"
```

Expected: **5/5 passed**.

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add content/za-vertical-slice/src/MyApp/Persistence/MoneyConverter.cs content/za-vertical-slice/tests/MyApp.UnitTests/Persistence/MoneyConverterTests.cs
git commit -m "feat(vs): port MoneyConverter into Persistence/ (closes #163 part 2)

Port of za-clean's MoneyConverter. ToStorage(Money) emits the canonical
'<amount>|<currency>' string under InvariantCulture; FromStorage parses
it back, defensively returning Money(0, 'USD') on empty / malformed
input (matches za-clean exactly).

5 unit tests cover: roundtrip preservation, fractional-pennies
precision, InvariantCulture stability, empty-string fallback,
malformed-string fallback. (za-clean is missing these tests today;
adding the same file there is left to a follow-up PR.)"
```

---

### Task 3: Switch Postgres migration to `text`

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Persistence/Migrations/Postgres/001_initial_schema.sql`

**Step 1: Replace the column type**

Open the file and replace the single line `"Total" numeric NOT NULL` with `"Total" text NOT NULL`:

```sql
-- Before:
"Total" numeric NOT NULL,
-- After:
"Total" text NOT NULL,
```

Use the Edit tool with `old_string: "Total" numeric NOT NULL,` and `new_string: "Total" text NOT NULL,`.

**Step 2: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj -c Release
```

Expected: green. (The SQL is an embedded resource; no compile impact from this change. Runtime check happens in Task 5.)

**Step 3: Revert + commit**

```powershell
git checkout global.json
git add content/za-vertical-slice/src/MyApp/Persistence/Migrations/Postgres/001_initial_schema.sql
git commit -m "feat(vs): switch Postgres Total column to text (uniform with Sqlite)

Aligns the Postgres storage contract with Sqlite for the Total column
(both now text), eliminating the divergent code path where the same
decimal binding routed to numeric on Postgres but TEXT on Sqlite.
The actual decimal-to-string conversion lands in the next commit
(handler rewiring via MoneyConverter)."
```

---

### Task 4: Rewire slice handlers + entities to use Money internally

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/ListOrders/ListOrders.cs`

> **Wire shape unchanged** — `PlaceOrderCommand.Total` stays `decimal` and `OrderDto.Total`/`OrderListItem.Total` stay `decimal`. Money lives strictly between the handler boundary and the database. This is intentional; see "Open-question resolution" at the top of the plan.

**Step 1: Update `PlaceOrder.cs`**

Three edits in this file:

**1a) Add the namespace using.** Find the existing using block at the top of the file. Add:

```csharp
using MyApp.Persistence;
```

(Should sort between `MyApp.Common` and `ZeroAlloc.*` — keep alphabetical with existing usings.)

**1b) Rewire the handler body** to wrap `cmd.Total` into Money before binding. Use Edit with:

- `old_string`:
  ```csharp
      public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
      {
          var id = await InsertOrderAsync(
              cmd.CustomerId.Value,
              cmd.Total,
              nameof(OrderStatus.Pending),
              ct).ConfigureAwait(false);
          return Result<OrderId, Error>.Success(new OrderId(id));
      }
  ```
- `new_string`:
  ```csharp
      public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
      {
          // Validator (`[GreaterThan(0)]` on Total) guarantees cmd.Total > 0,
          // so Money.TryCreate cannot fail here. Money is internal-only — the
          // wire surface (PlaceOrderCommand.Total) stays bare decimal.
          var money = Money.TryCreate(cmd.Total, "EUR").Value;
          var id = await InsertOrderAsync(
              cmd.CustomerId.Value,
              MoneyConverter.ToStorage(money),
              nameof(OrderStatus.Pending),
              ct).ConfigureAwait(false);
          return Result<OrderId, Error>.Success(new OrderId(id));
      }
  ```

**1c) Change the `InsertOrderAsync` partial method parameter type** from `decimal total` to `string total`:

- `old_string`:
  ```csharp
      [Command(
          "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"",
          Kind = CommandKind.Identity)]
      public partial Task<int> InsertOrderAsync(int customerId, decimal total, string status, CancellationToken ct);
  ```
- `new_string`:
  ```csharp
      [Command(
          "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"",
          Kind = CommandKind.Identity)]
      public partial Task<int> InsertOrderAsync(int customerId, string total, string status, CancellationToken ct);
  ```

> **NOTE on the `Order` entity (lines 85-118 of PlaceOrder.cs):** the class declares `decimal Total` but is **never instantiated anywhere** (verified via grep — `Order(` matches only the ctor declaration). It's documentation-shaped code showing "this is what the slice would persist if we had a domain entity to ferry around." Leave the `Order` class's `decimal Total` AS-IS — changing it to `Money` is symbolic-only (no behavior impact), and keeping it `decimal` minimizes the diff and mirrors what the wire surface is. If a future contributor actually instantiates `Order`, they can revisit.

**Step 2: Update `GetOrder.cs`**

Three edits:

**2a) Add namespace using:**

```csharp
using MyApp.Persistence;
```

**2b) Change `OrderRow`'s `Total` type from `decimal` to `string`:**

- `old_string`:
  ```csharp
      public sealed record OrderRow(int Id, int CustomerId, decimal Total);
  ```
- `new_string`:
  ```csharp
      // Total is stored as the MoneyConverter wire shape "<amount>|<currency>";
      // the handler converts it back to decimal for the DTO.
      public sealed record OrderRow(int Id, int CustomerId, string Total);
  ```

**2c) Rewire the handler's `OrderDto` projection** to convert the stored string back to decimal via MoneyConverter:

- `old_string`:
  ```csharp
          return row is null
              ? Result<OrderDto, Error>.Failure(Error.NotFound(
                  "order.not_found",
                  $"Order {query.Id.Value} not found"))
              : Result<OrderDto, Error>.Success(new OrderDto(
                  new OrderId(row.Id),
                  new CustomerId(row.CustomerId),
                  row.Total));
  ```
- `new_string`:
  ```csharp
          return row is null
              ? Result<OrderDto, Error>.Failure(Error.NotFound(
                  "order.not_found",
                  $"Order {query.Id.Value} not found"))
              : Result<OrderDto, Error>.Success(new OrderDto(
                  new OrderId(row.Id),
                  new CustomerId(row.CustomerId),
                  MoneyConverter.FromStorage(row.Total).Amount));
  ```

**Step 3: Update `ListOrders.cs`**

Three analogous edits:

**3a) Add namespace using:**

```csharp
using MyApp.Persistence;
```

**3b) Change `OrderListRow`'s `Total` type from `decimal` to `string`:**

- `old_string`:
  ```csharp
      private sealed record OrderListRow(int Id, int CustomerId, decimal Total);
  ```
- `new_string`:
  ```csharp
      // Total is stored as the MoneyConverter wire shape "<amount>|<currency>";
      // the handler converts it back to decimal for the wire DTO.
      private sealed record OrderListRow(int Id, int CustomerId, string Total);
  ```

**3c) Rewire the projection:**

- `old_string`:
  ```csharp
          var items = new List<OrderListItem>(rows.Count);
          foreach (var row in rows)
          {
              items.Add(new OrderListItem(new OrderId(row.Id), new CustomerId(row.CustomerId), row.Total));
          }
  ```
- `new_string`:
  ```csharp
          var items = new List<OrderListItem>(rows.Count);
          foreach (var row in rows)
          {
              items.Add(new OrderListItem(
                  new OrderId(row.Id),
                  new CustomerId(row.CustomerId),
                  MoneyConverter.FromStorage(row.Total).Amount));
          }
  ```

**Step 4: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj -c Release
```

Expected: green. ZA.ORM's generator should re-emit `InsertOrderAsync` with `string total` parameter binding (instead of decimal), and re-emit `ReadOrderAsync` + `ListOrdersAsync` with `string` reads from the `Total` column.

**If the build fails on the ZA.ORM generator side** (e.g. ZAO0xx diagnostic complaining about `string`-typed binding), STOP and investigate — likely missing convention support for `string` direct binding, which would be unexpected (it's the most basic case).

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs content/za-vertical-slice/src/MyApp/Features/Orders/ListOrders/ListOrders.cs
git commit -m "feat(vs): rewire Orders slices to use Money internally via MoneyConverter

PlaceOrder, GetOrder, and ListOrders now route money through the
Money VO + MoneyConverter at the persistence boundary. Public wire
shape (decimal Total) is preserved — Money exists strictly between
the handler entry and the database.

PlaceOrder: Money.TryCreate(cmd.Total, 'EUR') -> MoneyConverter.ToStorage
  -> InsertOrderAsync(string total).
GetOrder + ListOrders: row.Total now string; handler projects via
  MoneyConverter.FromStorage(...).Amount into the decimal DTO field.

ZA.ORM-emitted InsertOrderAsync / ReadOrderAsync / ListOrdersAsync
re-emit against string Total (the storage contract is now uniform
text-on-both-providers per the prior commit).

The Order entity class (PlaceOrder.cs lines 85-118) keeps decimal
Total — it's never instantiated anywhere; changing it would be
symbolic-only."
```

---

### Task 5: Verify integration tests + smoke tests still green

**Files:** none (verification only)

**Step 1: Run the full integration suite**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: **all tests pass**. The wire shape is unchanged (`{customerId, total: 99.99m}` POST, `dto.Total = 12.34m` GET), so existing endpoint tests should round-trip cleanly:
- `PlaceOrderEndpointTests` posts `total: 99.99m` → handler wraps to Money → stores `"99.99|EUR"` as TEXT → returns 201. No assertion on stored shape; just status code.
- `GetOrderEndpointTests` POSTs `total: 12.34m`, then GETs back → handler reads `"12.34|EUR"` → FromStorage(...).Amount = 12.34m → assertion `Assert.Equal(12.34m, dto.Total)` passes.
- `ListOrdersEndpointTests`, `CancelOrderEndpointTests` — similar story; no money assertions break.

**If any test fails**, the most likely cause is:
- (a) `decimal.ToString(InvariantCulture)` precision mismatch on roundtrip (e.g. `12.34m` becomes `"12.34"` becomes `12.34m` — should be exact, but verify by reading the failure)
- (b) `OrderRow`/`OrderListRow` shape change broke the ZA.ORM generator's emit (would surface earlier in Task 4's build verification, but possible if the integration test exercises a path the compile didn't)
- (c) JSON serialization of the now-unused `Money` VO confusing the AOT JsonContext (shouldn't fire since Money never crosses the wire boundary, but worth checking)

Investigate the actual failure before patching.

**Step 2: Run the full unit test suite**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj
```

Expected: all pass — includes the 4 new Money tests + 5 new MoneyConverter tests + any pre-existing unit tests.

**Step 3: Run the full convention test suite**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj
```

Expected: all pass. (Money + MoneyConverter live in `Common/` and `Persistence/` — both already-blessed locations per vs convention; no convention test should fire.)

**Step 4: Build the full template solution**

```powershell
dotnet build content/za-vertical-slice/MyApp.slnx -c Release
git checkout global.json
```

Expected: solution-wide build green.

**Step 5: No commit needed**

This task is verification-only. If everything's green, proceed to Task 6.

---

### Task 6: Push + PR + admin-merge

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected 5 commits (in order):
1. `docs(design): vs Money VO + MoneyConverter to close provider-divergence (#163)` (already on branch — `ea0c9ee`)
2. `feat(vs): port Money value object to Common/ (closes #163 part 1)`
3. `feat(vs): port MoneyConverter into Persistence/ (closes #163 part 2)`
4. `feat(vs): switch Postgres Total column to text (uniform with Sqlite)`
5. `feat(vs): rewire Orders slices to use Money internally via MoneyConverter`

**Step 2: Final full sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-vertical-slice/MyApp.slnx -c Release
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -c Release
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -c Release
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj -c Release
git checkout global.json
git status
```

Expected: build + all 3 test projects green, working tree clean.

**Step 3: Push**

```powershell
git push -u origin chore/vs-money-converter
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes #163. Adopts za-clean's Money VO + MoneyConverter pattern internally in `za-vertical-slice` so both providers store money uniformly as `text` (via the `"<amount>|<currency>"` wire shape), closing the `numeric`-on-Postgres vs `TEXT`-on-Sqlite divergence.

## What changes

- **Money VO** in `Common/Money.cs` — port of za-clean's Money. `[ValueObject]` struct, `TryCreate` rejects negative amount + empty currency.
- **MoneyConverter** in `Persistence/MoneyConverter.cs` — port of za-clean's converter. `ToStorage(Money) → "<amount>|<currency>"` under InvariantCulture; `FromStorage(string) → Money` with defensive fallbacks on empty/malformed input.
- **Postgres migration**: `Total numeric` → `Total text` (uniform with Sqlite — eliminates the two-contracts-behind-one-code-path issue).
- **Handlers rewired** in PlaceOrder / GetOrder / ListOrders: Money lives strictly at the persistence boundary; wire shape (`decimal Total`) preserved end-to-end.
- **Tests**: 4 new MoneyTests + 5 new MoneyConverterTests (the converter test file is new — za-clean is missing equivalent coverage; a symmetry PR is left as a follow-up).

## Wire shape preservation

Public API still takes `{customerId, total: 99.99m}` and returns `{id, customerId, total: 99.99m}`. Money is internal-only — converted in/out at the handler boundary. Currency hardcoded to `"EUR"` at the conversion site (vs has no multi-currency surface; matches za-clean's `unitPriceEur` convention).

## Existing integration tests pass unchanged

The wire shape didn't change, so `PlaceOrderEndpointTests`, `GetOrderEndpointTests`, `ListOrdersEndpointTests`, and `CancelOrderEndpointTests` all pass with no modifications. The Money conversion is verified end-to-end via these existing tests (POST then GET preserves the amount exactly).

## Intentionally NOT in this PR

- **Postgres integration test infrastructure** — neither template has Testcontainers-based Postgres coverage today; adding it is a separate infrastructure addition that benefits both templates. The Postgres storage path is "structurally guaranteed" after this PR (uniform SQL contract + unit-tested converter).
- **MoneyConverter unit tests for za-clean** — same tests should land there too for symmetry; left for a follow-up small PR.

## Note for release-please

This is a `feat:` commit; release-please will pick it up as a v0.12.0 minor bump. Squash title MUST start with `feat:` (recurring release-please gotcha — `chore:` squashes get skipped).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "feat(vs): adopt Money VO + MoneyConverter to close provider divergence (closes #163)" --body $prBody
```

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER>
```

Expected check set (matching PR #167's checks): `build`, `build-vs`, `real-run-smoke`, `real-run-smoke-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`. Wait for all to land green.

If a check fails, investigate before retrying. Most likely failure modes:
- AOT publish: the `[ValueObject]`-generated Money JSON converter might trip an AOT trimmer warning — surface it from the build log.
- Real-run smoke (which actually starts the app): startup migration fails if Postgres schema is misaligned. Should be fine because the smoke runs against Sqlite.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Critical:** squash *title* must start with `feat:`. `gh pr merge --squash` defaults to the PR title which is correct. Verify by checking the merged commit's title on `main` after the merge lands.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

Expected: new squashed `feat(vs): ...` commit on top.

**Step 8: Check release-please**

Wait 1-5 minutes:

```powershell
gh pr list --state open --search "release-please"
```

Expected: a PR titled something like `chore(main): release ZeroAlloc.Templates 0.12.0`. Capture its number. If it doesn't appear within 5 minutes, check `gh run list --workflow=release-please --limit 3` — but the squash title started with `feat:` so it should fire.

**Step 9: Confirm #163 closed**

```powershell
gh issue view 163 --json state -q .state
```

Expected: `CLOSED` (the `closes #163` in the PR body triggers auto-close on merge).

---

## Out of scope (deliberately not in this plan)

- za-clean's missing MoneyConverter unit tests — symmetric port, separate PR.
- Postgres integration test infrastructure for either template — bigger infra addition, separate issue.
- vs Order entity's `decimal Total` — class is never instantiated; change is symbolic-only.
- `Money` JSON wire shape — preserved as `decimal` in vs's public API, so no wire-format work needed.

## When the plan is complete

The branch `chore/vs-money-converter` has 5 commits (1 design + 4 implementation) + the merge squash on main. PR #163 is auto-closed. release-please has opened a v0.12.0 release PR rolling up this change.
