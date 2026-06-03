# `za-clean` Atomic Order Write (v2) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Adopt ZA.ORM v1.5's new `IAsyncDbTransaction` parameter on `za-clean`'s `OrderRepository.AddAsync` so the order head + lines persist atomically across all providers (Sqlite, Postgres, SqlClient), closing #162.

**Architecture:** Five tasks: (1) bump 3 ZA.ORM package pins from 1.2.0 → 1.5.0, (2) add `CHECK ("Quantity" > 0)` to both Sqlite + Postgres OrderLines migrations, (3) thread `IAsyncDbTransaction tx` parameter through `InsertOrderAsync` + `InsertOrderLineAsync` partial methods and rewrite `AddAsync` to open the connection / begin tx / run both inserts on that tx / commit (rollback on dispose-without-commit), (4) add a two-fact integration test exercising commit + CHECK-violation rollback paths, (5) push + PR + admin-merge with `fix:` squash title so release-please cuts a patch bump.

**Tech Stack:** .NET 10 / ZeroAlloc.ORM 1.5.0 (just published) / AdoNet.Async transaction surface / xUnit / Microsoft.Data.Sqlite (integration test fixture).

**Reference design doc:** `docs/plans/2026-06-03-za-clean-atomic-order-write-design.md` (committed `ef1df2d` on this branch).

**Working branch:** `fix/za-clean-atomic-order-write` (already created off `main` at `8a7edd9`).

> **Local SDK pin gotcha** (recurring): `global.json` pins SDK `10.0.300 latestMinor`; dev machine has 10.0.204 max. Before any `dotnet build`/`dotnet test`:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before commit. **Never commit the relaxed pin.**

> **NuGet propagation gotcha:** ZA.ORM v1.5.0 was just published. The `Abstractions` and `Generator` packages were visible on NuGet at design-approval time but the main `ZeroAlloc.ORM` flatcontainer index was still propagating (typical 5-15 min CDN lag from pack-push). **If `dotnet restore` fails after Task 1 with "package not found for 1.5.0", wait 5 minutes and retry** — do not roll back the pin.

---

### Task 1: Bump ZA.ORM pin to 1.5.0

**Files:**
- Modify: `Directory.Packages.props`

**Step 1: Read the existing pin block**

The 3 ZA.ORM pins live at lines ~30-32 with a leading comment around line 27. Confirm the current state — should read "ZeroAlloc.ORM 1.2.0 + AdoNet.Async substrate".

**Step 2: Bump the three version numbers**

Use Edit with the following pair:

- `old_string`:
  ```xml
      <!-- ZeroAlloc.ORM 1.2.0 + AdoNet.Async substrate (replaces EF Core 10).
  ```
- `new_string`:
  ```xml
      <!-- ZeroAlloc.ORM 1.5.0 + AdoNet.Async substrate (replaces EF Core 10).
  ```

Then bump the three `Version="1.2.0"` pins. Use Edit with `replace_all: true` for safety, or do them one at a time — they likely share `Version="1.2.0"` with no other 1.2.0 in the file. **Verify by Grep first** that no other package shares `Version="1.2.0"` exactly. If there's overlap, do them individually with surrounding context to disambiguate.

If unique, this Edit works:

- `old_string`: `Version="1.2.0"`
- `new_string`: `Version="1.5.0"`
- `replace_all`: `true`

If NOT unique (any other package pinned to 1.2.0), do the three pins individually with their surrounding `Include="ZeroAlloc.ORM..."` context.

**Step 3: Verify build green (template content + tests)**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet restore content/za-clean/MyApp.slnx
dotnet build content/za-clean/MyApp.slnx -c Release
```

Expected: green. If restore fails with "package ZeroAlloc.ORM 1.5.0 not found", that's the NuGet CDN propagation lag — wait 5 minutes and retry. Do not roll back the pin.

**Step 4: Verify existing tests still pass**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: all pass. ZA.ORM v1.5 is fully backwards-compatible with v1.2 (transaction parameter is OPTIONAL). No existing partial method declares a tx parameter, so the emit is byte-identical to v1.2.

**Step 5: Revert global.json + commit**

```powershell
git checkout global.json
git add Directory.Packages.props
git commit -m "chore(za-clean): bump ZA.ORM pin to 1.5.0

Unlocks the IAsyncDbTransaction parameter support shipped in ZA.ORM
v1.5.0 (release 2026-06-03). Backwards-compatible — no existing
partial method declares a tx parameter, so the emit is byte-identical
to v1.2."
```

---

### Task 2: Add `CHECK ("Quantity" > 0)` to both providers' migrations

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Sqlite/001_initial_schema.sql`
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Postgres/001_initial_schema.sql`

**Step 1: Edit Sqlite migration**

The OrderLines table's Quantity column. Use Edit:

- `old_string`:
  ```
      "Quantity" INTEGER NOT NULL,
  ```
- `new_string`:
  ```
      "Quantity" INTEGER NOT NULL CHECK ("Quantity" > 0),
  ```

**Step 2: Edit Postgres migration**

Use Edit:

- `old_string`:
  ```
      "Quantity" integer NOT NULL,
  ```
- `new_string`:
  ```
      "Quantity" integer NOT NULL CHECK ("Quantity" > 0),
  ```

**Step 3: Verify existing tests still pass**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: all pass — no existing test or seed creates an OrderLine with `Quantity = 0`.

**If any existing test fails** with a CHECK constraint violation, that test/seed has a Quantity = 0 somewhere — STOP and investigate.

**Step 4: Revert + commit**

```powershell
git checkout global.json
git add content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Sqlite/001_initial_schema.sql content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Postgres/001_initial_schema.sql
git commit -m "feat(za-clean): add Quantity > 0 CHECK to OrderLines (both providers)

Defensible invariant — order line with zero/negative quantity has no
business meaning. Also the failure-injection hook the rollback test
(Task 4) uses to verify atomic-write semantics.

Existing tests unaffected — none uses Quantity = 0."
```

---

### Task 3: Thread `IAsyncDbTransaction` through `OrderRepository`

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`

**Step 1: Add `using System.Data;` for `ConnectionState`**

Use Edit:

- `old_string`:
  ```csharp
  using System.Data.Async;
  using MyApp.Application;
  ```
- `new_string`:
  ```csharp
  using System.Data;
  using System.Data.Async;
  using MyApp.Application;
  ```

**Step 2: Add `IAsyncDbTransaction tx` to `InsertOrderAsync` partial method**

Find:

```csharp
    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Status\", \"Total\") VALUES (@customerId, @status, @total) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    private partial Task<int> InsertOrderAsync(int customerId, string status, string total, CancellationToken ct);
```

Use Edit, changing only the parameter list to insert `IAsyncDbTransaction tx` between `string total` and `CancellationToken ct`:

- `old_string`:
  ```csharp
      private partial Task<int> InsertOrderAsync(int customerId, string status, string total, CancellationToken ct);
  ```
- `new_string`:
  ```csharp
      private partial Task<int> InsertOrderAsync(int customerId, string status, string total, IAsyncDbTransaction tx, CancellationToken ct);
  ```

**Step 3: Add `IAsyncDbTransaction tx` to `InsertOrderLineAsync` partial method**

Find:

```csharp
    [Command(
        "INSERT INTO \"OrderLines\" (\"OrderId\", \"Sku\", \"Quantity\", \"Price\") VALUES (@orderId, @sku, @quantity, @price)")]
    private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, CancellationToken ct);
```

Use Edit:

- `old_string`:
  ```csharp
      private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, CancellationToken ct);
  ```
- `new_string`:
  ```csharp
      private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, IAsyncDbTransaction tx, CancellationToken ct);
  ```

**Step 4: Rewrite the `AddAsync` body**

Use Edit:

- `old_string`:
  ```csharp
      public async Task AddAsync(Order order, CancellationToken ct)
      {
          var orderId = await InsertOrderAsync(
              order.CustomerId.Value,
              order.Status.ToString(),
              MoneyConverter.ToStorage(order.Total),
              ct).ConfigureAwait(false);

          order.AssignPersistenceId(new OrderId(orderId));

          foreach (var line in order.Lines)
          {
              await InsertOrderLineAsync(
                  orderId,
                  line.Sku,
                  line.Quantity,
                  MoneyConverter.ToStorage(line.Price),
                  ct).ConfigureAwait(false);
          }
      }
  ```
- `new_string`:
  ```csharp
      public async Task AddAsync(Order order, CancellationToken ct)
      {
          // BeginTransactionAsync requires an open connection. The per-command
          // ref-counted prologue inside each emitted partial method sees
          // State == Open and is a no-op for both connection lifecycle and
          // transaction state — the tx attaches via cmd.Transaction (v1.5+),
          // not via auto-bind, so SqlClient works correctly too.
          var openedHere = conn.State != ConnectionState.Open;
          if (openedHere) await conn.OpenAsync(ct).ConfigureAwait(false);
          try
          {
              await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

              var orderId = await InsertOrderAsync(
                  order.CustomerId.Value,
                  order.Status.ToString(),
                  MoneyConverter.ToStorage(order.Total),
                  tx,
                  ct).ConfigureAwait(false);

              order.AssignPersistenceId(new OrderId(orderId));

              foreach (var line in order.Lines)
              {
                  await InsertOrderLineAsync(
                      orderId,
                      line.Sku,
                      line.Quantity,
                      MoneyConverter.ToStorage(line.Price),
                      tx,
                      ct).ConfigureAwait(false);
              }

              await tx.CommitAsync(ct).ConfigureAwait(false);
              // Exception above propagates out of the `using` scope — tx
              // DisposeAsync rolls back if CommitAsync wasn't reached.
          }
          finally
          {
              if (openedHere) await conn.CloseAsync().ConfigureAwait(false);
          }
      }
  ```

**Step 5: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj -c Release
```

Expected: green. ZA.ORM v1.5's generator emits `__cmd.Transaction = @tx;` for the two partial methods now that they declare the tx parameter.

**Step 6: Verify existing integration tests still pass**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: all pass — `CreateOrderEndpointTests.POST_orders_returns_201_with_jwt` exercises the same happy path; the transaction wrap is semantic no-op on success.

**If a test fails**, the most likely culprit is the per-command ref-counted prologue interacting unexpectedly with a pre-opened connection. `MyAppFactory.cs:67-78` explicitly documents this works, so STOP and investigate before committing.

**Step 7: Revert + commit**

```powershell
git checkout global.json
git add content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs
git commit -m "fix(za-clean): thread IAsyncDbTransaction through OrderRepository.AddAsync (closes #162)

After the EF -> ZA.ORM swap (PR #152), AddAsync wrote the order head
plus each order line as separate autocommitted commands. A failure
mid-loop persisted a corrupt aggregate.

ZA.ORM v1.5 (PR #111) added optional IAsyncDbTransaction parameter
support on [Command] partial methods. Thread an explicit tx through
InsertOrderAsync + InsertOrderLineAsync. Wrap AddAsync in
conn.BeginTransactionAsync -- commit on success, dispose rolls back
on exception. Open the connection ourselves first because
BeginTransactionAsync requires it (the per-command ref-counted
prologue sees State == Open and is a no-op).

Provider-portable: cmd.Transaction is set explicitly on every
command, so Sqlite + Postgres + SqlClient all participate in the
transaction correctly. vs is untouched -- its PlaceOrder is
single-statement and already atomic."
```

---

### Task 4: Atomicity integration tests

**Files:**
- Create: `content/za-clean/tests/MyApp.IntegrationTests/OrderRepositoryAtomicityTests.cs`

**Step 1: Write the two tests**

Each test creates its own `MyAppFactory` to start with an empty in-memory database — atomicity is observable via post-failure row counts.

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Verifies that <see cref="MyApp.Infrastructure.Persistence.OrderRepository.AddAsync"/>
/// writes the Order aggregate atomically: a failure inserting any line rolls
/// back the order head and every previously-inserted line.
///
/// Each test creates its own <see cref="MyAppFactory"/> so atomicity is
/// observable via post-failure row counts (no shared-fixture seed pollution).
/// Failure is injected by a CHECK ("Quantity" > 0) constraint violation
/// on the second order line.
/// </summary>
public sealed class OrderRepositoryAtomicityTests
{
    [Fact]
    public async Task AddAsync_rolls_back_order_head_and_lines_when_line_insert_fails()
    {
        using var factory = new MyAppFactory();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-OK", 1, Money.TryCreate(10m, "EUR").Value);
        // Quantity = 0 violates the CHECK ("Quantity" > 0) constraint added
        // in the prior commit. The Sqlite provider raises SqliteException
        // with the constraint message; the transaction's DisposeAsync
        // (via the `await using` scope inside AddAsync) rolls back the
        // order head and the first OK line.
        order.AddLine("SKU-INVALID", 0, Money.TryCreate(5m, "EUR").Value);

        await Assert.ThrowsAsync<SqliteException>(() => repo.AddAsync(order, CancellationToken.None));

        // Atomicity: nothing persisted.
        var orderCount = await repo.CountAsync(CancellationToken.None);
        Assert.Equal(0, orderCount);
    }

    [Fact]
    public async Task AddAsync_commits_order_head_and_lines_when_all_inserts_succeed()
    {
        // Sibling-positive guard against the transaction wrap accidentally
        // rolling back successful writes. Verifies the commit path is
        // exercised when no constraint violates.
        using var factory = new MyAppFactory();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = Order.Create(new CustomerId(42));
        order.AddLine("SKU-A", 2, Money.TryCreate(10m, "EUR").Value);
        order.AddLine("SKU-B", 3, Money.TryCreate(5m, "EUR").Value);

        await repo.AddAsync(order, CancellationToken.None);

        var orderCount = await repo.CountAsync(CancellationToken.None);
        Assert.Equal(1, orderCount);
    }
}
```

**Step 2: Run the new tests**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj --filter "FullyQualifiedName~OrderRepositoryAtomicityTests"
```

Expected: **2/2 passed**.

**If the rollback test fails** with `orderCount == 1` post-throw: the transaction is NOT actually rolling back. The most likely cause is that ZA.ORM's emitted code somehow drops the `cmd.Transaction = @tx` assignment. To debug, inspect the generated source under `obj/Debug/net10.0/generated/ZeroAlloc.ORM.Generator/...` and verify the `__cmd.Transaction = @tx;` line is present in both InsertOrderAsync and InsertOrderLineAsync emitted bodies.

**If the commit test fails** with `orderCount == 0`: the commit isn't being reached. Inspect AddAsync's code path.

STOP if either fails — don't commit a broken state.

**Step 3: Verify the full integration suite still green**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: existing 3 + 2 new = 5/5 pass.

**Step 4: Revert + commit**

```powershell
git checkout global.json
git add content/za-clean/tests/MyApp.IntegrationTests/OrderRepositoryAtomicityTests.cs
git commit -m "test(za-clean): atomicity assertion for AddAsync rollback path

Two integration tests guard the transaction semantics from the
prior commit:

  - rollback path: order with two lines, second has Quantity = 0
    which trips the CHECK constraint. Asserts SqliteException
    thrown; asserts Orders count == 0 (head rolled back along
    with the first line).

  - commit path: sibling-positive guard against the transaction
    wrap accidentally rolling back successful writes.

Each test owns its own MyAppFactory so the row-count assertions
are not polluted by sibling-test seed data."
```

---

### Task 5: Push + PR + admin-merge with `fix:` squash title

**Step 1: Pre-flight commit log check**

```powershell
git log --oneline main..HEAD
```

Expected 5 commits in order:
1. `docs(design): za-clean atomic order write v2 — adopt ZA.ORM v1.5 tx parameter` (already on branch — `ef1df2d`)
2. `chore(za-clean): bump ZA.ORM pin to 1.5.0`
3. `feat(za-clean): add Quantity > 0 CHECK to OrderLines (both providers)`
4. `fix(za-clean): thread IAsyncDbTransaction through OrderRepository.AddAsync (closes #162)`
5. `test(za-clean): atomicity assertion for AddAsync rollback path`

**Step 2: Final full sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build content/za-clean/MyApp.slnx -c Release
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -c Release
git checkout global.json
git status
```

Expected: build green, all 3 test projects green, working tree clean.

**Step 3: Push**

```powershell
git push -u origin fix/za-clean-atomic-order-write
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes #162. Adopts ZA.ORM v1.5''s new `IAsyncDbTransaction` parameter support in za-clean''s `OrderRepository.AddAsync` so the order head + lines persist atomically across all providers (Sqlite, Postgres, SqlClient).

## What changes

- **`Directory.Packages.props`**: bump 3 ZA.ORM pins from `1.2.0` → `1.5.0` (unlocks the IAsyncDbTransaction parameter feature).
- **OrderLines schema** (both providers): `CHECK ("Quantity" > 0)` constraint. Defensible invariant + the failure-injection hook the rollback test uses.
- **`InsertOrderAsync` + `InsertOrderLineAsync`** partial methods: add `IAsyncDbTransaction tx` parameter. ZA.ORM v1.5 detects the parameter and emits `__cmd.Transaction = @tx;` at every command site.
- **`AddAsync`**: explicit open / `BeginTransactionAsync` / commit-on-success / rollback-on-dispose. Open is needed because `BeginTransactionAsync` requires an open connection; the per-command ref-counted prologue sees `State == Open` and is a no-op.
- **Two integration tests**:
  - **rollback**: order with two lines, second has `Quantity = 0` → trips CHECK constraint → asserts `SqliteException` + Orders count == 0
  - **commit**: sibling-positive guard against the wrap accidentally rolling back successful writes

## Provider portability

ZA.ORM v1.5 emits `cmd.Transaction = @tx` explicitly, so Sqlite + Postgres + SqlClient all participate in the transaction correctly. No reliance on Sqlite/Postgres auto-bind behavior (the silent-breakage path on SqlClient).

## vs untouched

vs''s `PlaceOrder` is a single `INSERT ... RETURNING Id` statement — atomic by virtue of being one statement. No non-atomic write to fix.

## Test plan

- [x] Existing happy-path tests pass (transaction wrap is semantic no-op on success)
- [x] New rollback test asserts `SqliteException` thrown + `Orders.COUNT == 0`
- [x] New commit-path sibling test asserts commit reached + `Orders.COUNT == 1`

## Note for release-please

`fix:` commit; release-please will pick this up as a patch bump (v0.12.1) if the currently-open PR #169 (proposing v0.12.0) refreshes to include it, or as a separate v0.12.1 PR otherwise. **Squash title MUST start with `fix:`** (recurring release-please gotcha).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "fix(za-clean): adopt ZA.ORM v1.5 transaction parameter — close non-atomic Order write (closes #162)" --body $prBody
```

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected check set: `build`, `build-vs`, `real-run-smoke`, `real-run-smoke-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`. All should land green — the change is provider-portable and the existing tests prove it doesn't regress the happy path.

If `real-run-smoke` fails on startup, suspect the new CHECK constraint syntax — inspect the log.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Critical**: squash *title* must start with `fix:`. The PR title `fix(za-clean): ...` already does — `gh pr merge --squash`'s default-to-PR-title behavior is correct.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

Expected: new squashed `fix(za-clean): ...` commit on top of main.

**Step 8: Check release-please**

```powershell
Start-Sleep -Seconds 60
gh pr list --state open --search "release-please"
```

Two possible outcomes:
- (a) PR #169 (v0.12.0) refreshes to include this fix
- (b) A separate v0.12.1 release PR opens

Either is acceptable. Capture the resulting PR number.

**Step 9: Confirm #162 closed**

```powershell
gh issue view 162 --json state -q .state
```

Expected: `CLOSED`.

---

## Out of scope (deliberately not in this plan)

- vs adoption (vs already atomic — single-statement)
- TxScope reusable helper (YAGNI for one call site)
- za-clean MoneyConverter test symmetry PR (separate carry-forward)
- AGENTS.md update (inline code comment is enough)
- Postgres integration test infrastructure (neither template has it; separate infra PR)

## When the plan is complete

The branch `fix/za-clean-atomic-order-write` has 5 commits (1 design + 4 implementation) + the merge squash on main. PR #162 is auto-closed. release-please has either refreshed PR #169 to include this fix or opened a v0.12.1 PR.
