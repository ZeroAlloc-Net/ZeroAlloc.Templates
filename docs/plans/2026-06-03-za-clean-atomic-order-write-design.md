# `za-clean` Atomic Order Write (v2 — ORM-supported) — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.Templates `content/za-clean/`, template-only
**Closes:** #162
**Branch:** `fix/za-clean-atomic-order-write` off `main` at `8a7edd9`
**Depends on:** ZeroAlloc.ORM v1.5.0 (`IAsyncDbTransaction` parameter support — shipped 2026-06-03)

## Background

After the EF Core → ZA.ORM swap (PR #152), za-clean's `OrderRepository.AddAsync` writes the order head + lines as separate autocommitted commands. A failure mid-loop persists a corrupt aggregate. The initial fix was a template-side workaround (`BeginTransactionAsync` + rely on Sqlite/Postgres auto-binding), but that left SqlClient silently broken and added 25 lines of boilerplate per multi-statement aggregate write.

The fix was pushed down to ZA.ORM instead. **v1.5.0 added `IAsyncDbTransaction` parameter support** on `[Command]` / `[Query]` / `[StoredProcedure]` partial methods: when a parameter of type `IAsyncDbTransaction` is present, the generator emits `__cmd.Transaction = @<paramName>;` at every command and batch site. Provider-portable: works on Sqlite, Postgres, SqlClient, MySQL alike.

This PR adopts that feature in za-clean.

## Decision

Bump the ZA.ORM pin to 1.5.0. Thread an `IAsyncDbTransaction tx` parameter through `InsertOrderAsync` + `InsertOrderLineAsync`. Rewrite `AddAsync` to open the connection once, begin a transaction, run both partial methods explicitly attached to that transaction, commit on success / rollback on dispose. Add a `CHECK ("Quantity" > 0)` constraint to both providers' OrderLines migration (defensible invariant + the failure-injection hook for the rollback test). Add an integration test exercising the rollback path.

## What changes

**Files modified (4):**

1. **`Directory.Packages.props`** — bump 3 ZA.ORM pins from `1.2.0` to `1.5.0`. Update the inline comment from "ZeroAlloc.ORM 1.2.0" to "ZeroAlloc.ORM 1.5.0":
   ```xml
   <PackageVersion Include="ZeroAlloc.ORM" Version="1.5.0" />
   <PackageVersion Include="ZeroAlloc.ORM.Abstractions" Version="1.5.0" />
   <PackageVersion Include="ZeroAlloc.ORM.Generator" Version="1.5.0" />
   ```

2. **`content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`**:
   - Add `IAsyncDbTransaction tx` to the partial method signatures (between the SQL value parameters and `CancellationToken ct`).
   - Replace `AddAsync` body with the explicit-transaction shape:
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
                 order.CustomerId.Value, order.Status.ToString(),
                 MoneyConverter.ToStorage(order.Total), tx, ct).ConfigureAwait(false);
             order.AssignPersistenceId(new OrderId(orderId));
             foreach (var line in order.Lines)
             {
                 await InsertOrderLineAsync(orderId, line.Sku, line.Quantity,
                     MoneyConverter.ToStorage(line.Price), tx, ct).ConfigureAwait(false);
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

3. **`Migrations/Sqlite/001_initial_schema.sql`** — `OrderLines.Quantity`: `INTEGER NOT NULL` → `INTEGER NOT NULL CHECK ("Quantity" > 0)`.

4. **`Migrations/Postgres/001_initial_schema.sql`** — `OrderLines."Quantity"`: `integer NOT NULL` → `integer NOT NULL CHECK ("Quantity" > 0)`.

**Files created (1):**

5. **`content/za-clean/tests/MyApp.IntegrationTests/OrderRepositoryAtomicityTests.cs`** — two integration tests:
   - **rollback path**: build an order with two lines, the second carrying `Quantity = 0` so the CHECK constraint trips on the line insert. Invoke `AddAsync`; assert `SqliteException` thrown; assert `repo.CountAsync == 0` afterward (the order head AND the first line were rolled back).
   - **commit path** (sibling-positive guard): build an order with two valid lines, invoke `AddAsync` successfully, assert `repo.CountAsync == 1`.

Each test instantiates its own `MyAppFactory` to start with an empty DB — atomicity is observable via row counts.

## Why honest about the open-dance

The "clean 5-line shape" was aspirational. The actual production code is ~15 lines because `BeginTransactionAsync` requires an open connection, and za-clean's DI registers `IAsyncDbConnection` such that the per-command ref-counted prologue opens/closes per call. Hiding the open-dance behind a helper (a `TxScope` utility) would save lines but obscure the connection lifecycle — adopters reading the template benefit from seeing what's happening.

The real win is **provider portability + visibility**:
- Each command's tx membership is explicit (`tx` parameter at the call site, `cmd.Transaction = @tx` in the generated body)
- SqlClient adopters get atomic writes without the silent auto-bind dependency
- The diagnostic shape (`ZAO080` for multiple tx parameters) tells adopters when they've miswired

## Commit shape

Four commits (kept granular for review):
1. `chore(za-clean): bump ZA.ORM pin to 1.5.0`
2. `feat(za-clean): add Quantity > 0 CHECK to OrderLines (both providers)`
3. `fix(za-clean): thread IAsyncDbTransaction through OrderRepository.AddAsync (closes #162)`
4. `test(za-clean): atomicity assertion for AddAsync rollback path`

Squash title: `fix:` (cuts a v0.12.1 patch via release-please). The open release-please PR #169 (currently v0.12.0) will refresh to roll this in.

## What stays out of scope

- **vs adoption** — vs's `PlaceOrder` is a single `INSERT ... RETURNING Id` statement, already atomic by virtue of being one statement.
- **TxScope reusable helper** — YAGNI for one call site.
- **za-clean MoneyConverter test symmetry PR** — separate carry-forward.
- **AGENTS.md update** — adopters can read the inline code comment.

## Acceptance criteria (from #162)

- [x] `AddAsync` is atomic: a failure inserting any line rolls back the order head.

## Risk

- **NuGet CDN propagation lag**: the v1.5.0 `ZeroAlloc.ORM` flatcontainer index was lagging at the time this design was approved (CDN index update typically settles within 15 minutes of pack-push). If the implementer hits a "package not found" during `dotnet restore`, wait a few minutes and retry.
