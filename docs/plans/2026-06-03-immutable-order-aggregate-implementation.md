# `za-clean` Immutable Order Aggregate Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Remove `Order.AssignPersistenceId` mutator + make `Order.Id` truly immutable; the repository returns a new `Order` instance via `Order.Materialize()` carrying the DB-assigned id, closing #166.

**Architecture:** Two tasks: (1) atomic refactor — five interlinked edits across Domain / Application / Infrastructure / Api projects, committed together because intermediate states don't build; (2) push + PR + admin-merge with `refactor:` squash title for v0.13.1 release-please patch bump.

**Tech Stack:** .NET 10 / `Order` domain aggregate / `IOrderRepository` interface / ZeroAlloc.ORM 1.5.0.

**Reference design doc:** `docs/plans/2026-06-03-immutable-order-aggregate-design.md` (committed `d348f00` on this branch).

**Working branch:** `refactor/za-clean-immutable-order` (already created off `main` at `6222fb0`).

> **Local SDK pin gotcha** (TWO files): both `global.json` and `content/za-clean/global.json` pin `10.0.300`. Before any `dotnet` invocation:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> (Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
> ```
> Revert BOTH before commit:
> ```powershell
> git checkout global.json content/za-clean/global.json
> ```
> The nested file was the gotcha that broke an earlier subagent run.

> **Atomic-refactor note:** the 5 file changes are interlinked — `Order.cs` removes `AssignPersistenceId`, `OrderRepository.cs` is the only caller. `IOrderRepository.cs` changes the `AddAsync` return type, `OrderRepository.cs` implements the new signature, and 2 call sites consume the new return. Build is green only when all 5 edits land together. **One commit, not five.**

---

### Task 1: Apply the 5-file atomic refactor

**Files:**
- Modify: `content/za-clean/src/MyApp.Domain/Order.cs`
- Modify: `content/za-clean/src/MyApp.Application/IOrderRepository.cs`
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`
- Modify: `content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderHandler.cs`
- Modify: `content/za-clean/src/MyApp.Api/SeedData.cs`

#### Step 1.1: `Order.cs` — remove the leak

Read the file first to confirm the current shape (especially the property declarations and the parameterless ctor). Then three edits:

**Edit A — remove the comment + private setter + the `AssignPersistenceId` method.**

Use Edit with:

- `old_string`:
  ```csharp
      // Setter is private — only AssignPersistenceId can mutate Id, and only from
      // its sentinel-zero pre-insert state. Domain code must not touch this.
      public OrderId Id { get; private set; }

      /// <summary>
      /// Persistence-layer hook for assigning the database-generated identity
      /// after a successful INSERT...RETURNING. Throws if the id has already
      /// been assigned — orders are immutable once persisted.
      /// </summary>
      public void AssignPersistenceId(OrderId id)
      {
          if (Id.Value != 0)
          {
              throw new InvalidOperationException("Order Id is already assigned");
          }
          Id = id;
      }
  ```
- `new_string`:
  ```csharp
      // Id is set once at construction. Order.Create() seeds the sentinel
      // OrderId(0); OrderRepository.AddAsync returns a new Order built via
      // Order.Materialize(...) carrying the DB-assigned id.
      public OrderId Id { get; }
  ```

**Edit B — remove the parameterless ctor (dead post-EF-swap).** Use Edit with:

- `old_string`:
  ```csharp
      // EF Core materialisation constructor. The framework rehydrates [CustomerId]
      // and [Total] through the configured property/owned-type mappings; the field
      // initialisers above keep [_lines] non-null and EF assigns OrderStatus via
      // its value-converter.
      private Order()
      {
      }
  ```
- `new_string`: (empty string — delete the entire block including the leading blank line if present)

> If the surrounding whitespace doesn't match exactly, use a Read to see the file and adjust the `old_string` accordingly.

**Verify (no commit yet):**

Look at the resulting `Order.cs`. Confirm:
- `public OrderId Id { get; }` (no setter, no `private set`)
- No `AssignPersistenceId` method
- No parameterless `private Order()` ctor
- The 2-arg `private Order(OrderId id, CustomerId customerId)` ctor still exists
- `Order.Create`, `AddLine`, `Cancel`, `Order.Materialize` all still exist
- The build won't compile yet (`OrderRepository.cs` still calls the removed method)

#### Step 1.2: `IOrderRepository.cs` — change the return type

Use Edit with:

- `old_string`:
  ```csharp
      Task AddAsync(Order order, CancellationToken ct);
  ```
- `new_string`:
  ```csharp
      /// <summary>
      /// Persists the order aggregate and returns a new <see cref="Order"/>
      /// instance carrying the DB-assigned id. The input <paramref name="order"/>
      /// is unchanged — its <c>Id</c> remains at the sentinel <c>OrderId(0)</c>
      /// from <see cref="Order.Create"/>. Callers must use the returned instance.
      /// </summary>
      Task<Order> AddAsync(Order order, CancellationToken ct);
  ```

#### Step 1.3: `OrderRepository.cs` — materialize + return

Read the current `AddAsync` body (likely lines 13-40 after the #162 transaction work). Then update both the signature and the body.

Use Edit with:

- `old_string`:
  ```csharp
      public async Task AddAsync(Order order, CancellationToken ct)
  ```
- `new_string`:
  ```csharp
      public async Task<Order> AddAsync(Order order, CancellationToken ct)
  ```

Then update the body — find the line `order.AssignPersistenceId(new OrderId(orderId));` and the subsequent foreach loop. The materialize + return goes after the line inserts complete and before (or after — doesn't matter) `tx.CommitAsync(ct)`.

Use Edit with:

- `old_string`:
  ```csharp
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
  ```
- `new_string`:
  ```csharp
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
              return Order.Materialize(
                  new OrderId(orderId),
                  order.CustomerId,
                  order.Status,
                  order.Total,
                  order.Lines);
  ```

> **NOTE:** the `try` block's tail used to be `await tx.CommitAsync(ct).ConfigureAwait(false);` with no `return` (since the method was `async Task`). The new method is `async Task<Order>` so it MUST return on all happy-path branches. The `return` goes after the commit so the materialized instance reflects the committed state.

#### Step 1.4: `CreateOrderHandler.cs` — capture the returned instance

Read the file to confirm lines 40-41:

```csharp
await repo.AddAsync(order, ct).ConfigureAwait(false);
return Result<OrderId, ApplicationError>.Success(order.Id);
```

Use Edit:

- `old_string`:
  ```csharp
          await repo.AddAsync(order, ct).ConfigureAwait(false);
          return Result<OrderId, ApplicationError>.Success(order.Id);
  ```
- `new_string`:
  ```csharp
          var persisted = await repo.AddAsync(order, ct).ConfigureAwait(false);
          return Result<OrderId, ApplicationError>.Success(persisted.Id);
  ```

#### Step 1.5: `SeedData.cs` — explicit discard

Use Edit:

- `old_string`:
  ```csharp
          await repo.AddAsync(order, ct).ConfigureAwait(false);
  ```
- `new_string`:
  ```csharp
          _ = await repo.AddAsync(order, ct).ConfigureAwait(false);
  ```

#### Step 1.6: Build + verify all tests green

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
(Get-Content content/za-clean/global.json) -replace '10\.0\.300','10.0.100' | Set-Content content/za-clean/global.json
dotnet build content/za-clean/MyApp.slnx -c Release
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -c Release
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -c Release
git checkout global.json content/za-clean/global.json
git status
```

Expected:
- Build: green
- Unit: all pass
- Integration: 5/5 pass (3 baseline + 2 atomicity)
- Architecture: all pass (dependency direction unchanged; `AssignPersistenceId` removal is structural improvement)
- Working tree clean (both global.json files reverted)

**If a test fails**, the most likely cause is:
- A caller of `order.Id` between `AddAsync` and the return — verify both `CreateOrderHandler` and `SeedData` were updated (Steps 1.4 + 1.5)
- An integration test that reads `order.Id` post-`AddAsync` — verify `OrderRepositoryAtomicityTests` doesn't do this (it shouldn't; commit/rollback tests use `repo.CountAsync`, not the input order's Id)

STOP and report any failure — don't commit a broken state.

#### Step 1.7: Commit

```powershell
git add content/za-clean/src/MyApp.Domain/Order.cs
git add content/za-clean/src/MyApp.Application/IOrderRepository.cs
git add content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs
git add content/za-clean/src/MyApp.Application/CreateOrder/CreateOrderHandler.cs
git add content/za-clean/src/MyApp.Api/SeedData.cs
git commit -m "refactor(za-clean): immutable Order aggregate — remove persistence leak (closes #166)

Remove Order.AssignPersistenceId mutator + the dead EF-era parameterless
ctor. Order.Id is now truly immutable (get;).

IOrderRepository.AddAsync now returns Task<Order> — the persisted
instance, materialized via Order.Materialize() carrying the DB-assigned
id. The input order's Id stays at the sentinel OrderId(0) sentinel;
callers must use the returned instance.

The Domain layer is now properly free of persistence concerns. Existing
architecture tests (dependency direction) continue to enforce the
boundary structurally; removing the mutator IS the architectural
improvement. NetArchTest can't easily express 'no persistence-aware
mutators' so no new test added.

Caller-side changes:
  - CreateOrderHandler: capture persisted, return persisted.Id
  - SeedData: explicit discard via _ = ...

DB-generated identity stays; schema unchanged."
```

## Self-review

- `git status` clean (only untracked `.bench-artifacts/`)
- `git diff HEAD~1 HEAD -- global.json content/za-clean/global.json` empty
- `git show HEAD --stat` shows exactly 5 source files
- All 3 test projects pass

---

### Task 2: Push + PR + admin-merge

**Step 2.1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected 2 commits:
1. `d348f00` docs(design): immutable Order aggregate to close #166
2. `<NEW>` refactor(za-clean): immutable Order aggregate — remove persistence leak (closes #166)

**Step 2.2: Push**

```powershell
git push -u origin refactor/za-clean-immutable-order
```

**Step 2.3: Open the PR**

```powershell
$prBody = @'
## Summary

Closes #166. Removes `Order.AssignPersistenceId` mutator from the Domain layer; repository returns a new `Order` instance via `Order.Materialize()` carrying the DB-assigned id. `Order.Id` is now truly immutable.

## What changes

Five files, atomic refactor:

- **`Order.cs`**: drop `AssignPersistenceId`, drop the `private set` on `Id`, drop the dead EF-era parameterless `private Order()` ctor. `Create`, `AddLine`, `Cancel`, `Materialize` unchanged.
- **`IOrderRepository.cs`**: `AddAsync` signature `Task` → `Task<Order>` (returns the persisted instance).
- **`OrderRepository.cs`**: `AddAsync` builds + returns the persisted Order via `Order.Materialize(new OrderId(orderId), order.CustomerId, order.Status, order.Total, order.Lines)` after commit.
- **`CreateOrderHandler.cs`**: captures the returned instance (`var persisted = await repo.AddAsync(...)`) and returns `persisted.Id`.
- **`SeedData.cs`**: explicit discard via `_ = await repo.AddAsync(...)`.

## Why now

za-clean advertises NetArchTest-enforced Clean Architecture but `AssignPersistenceId` was a public mutator on a Domain aggregate that existed solely for the repository's benefit — exactly the kind of leak the boundary tries to keep out. Removing it removes the leak. DB-generated identity stays; the schema is unchanged. The architecture-tests boundary stays structurally enforced.

## What about NetArchTest?

NetArchTest enforces dependency direction (`Domain` has no `Infrastructure`/`Application`/`Api`/EF/AspNet deps). It can't easily express "no mutators that look persistence-aware" — that kind of rule is brittle. Removing the mutator IS the architectural improvement; the existing rules continue to enforce the structural boundary.

## Caller-side footgun (deliberate)

Adopters who don't capture the return value but rely on `order.Id` will silently get the sentinel `OrderId(0)`. That's a deliberate design choice — forcing callers to think about which instance carries the assigned id is the architectural point. The XML doc on `IOrderRepository.AddAsync` calls this out explicitly.

## Test plan

- [x] Build green
- [x] Unit tests pass (no behavior change to non-persistence code)
- [x] Integration tests pass — 5/5 (3 baseline + 2 atomicity from #162)
- [x] Architecture tests pass — dependency direction unchanged
- [ ] CI: build + build-vs + real-run-smoke + real-run-smoke-vs + aot-publish-smoke + aot-publish-smoke-vs

## Note for release-please

`refactor:` commit → patch bump (v0.13.1). **Squash title MUST start with `refactor:`** (recurring release-please gotcha; non-feat/fix/chore types are recognized but must be the squash prefix to fire).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "refactor(za-clean): immutable Order aggregate — remove persistence leak (closes #166)" --body $prBody
```

Capture the PR number.

**Step 2.4: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected check set: `build`, `build-vs`, `real-run-smoke`, `real-run-smoke-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`. All should land green — the refactor is internal to za-clean's src; the CI matrix exercises both templates but only za-clean changed.

If any check fails, investigate the log before retrying.

**Step 2.5: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Critical**: the squash *title* must start with `refactor:`. The PR title already does — `gh pr merge --squash`'s default-to-PR-title is correct.

**Step 2.6: Verify post-merge + #166 closure**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
gh issue view 166 --json state -q .state
```

Expected: new squashed `refactor(za-clean): ...` commit on top of main; #166 state `CLOSED` (auto-closed by `closes #166`).

**Step 2.7: Wait for release-please**

```powershell
Start-Sleep -Seconds 60
gh pr list --state open --search "release-please"
```

Expected: `chore(main): release ZeroAlloc.Templates 0.13.1` PR opened (or an existing one refreshes).

> **Watch out:** release-please's behavior on `refactor:` commits varies by config. Conventional Commits treats `refactor` as a "non-breaking change with no version impact by default," but many release-please configs map `refactor` to a patch bump. If no PR opens within ~3 minutes, check `.release-please-config.json` (or equivalent) — if `refactor` isn't in its `release-as-types`, this PR's change will just sit on main until the next `feat`/`fix` flushes it. That's not a problem (the change is on main and works) but it's worth knowing about.

## Report

- Final test counts (unit/integration/architecture)
- PR URL/number
- CI check results
- Merge SHA on `main`
- #166 final state
- release-please PR number (or note if `refactor:` doesn't trigger one)
- Anything unexpected

Do NOT push fixes blindly to CI failures. Investigate first.

---

## Out of scope (deliberately not in this plan)

- vs Order entity (documentation-shaped, never instantiated)
- Schema changes / client-generated IDs (Approach B from brainstorm)
- NetArchTest rule for "no persistence mutators" (brittle to express; not warranted)
- README update (no claim to scope back)

## When the plan is complete

The branch `refactor/za-clean-immutable-order` has 3 commits (1 design + 1 refactor + 1 merge squash on main). #166 auto-closed. release-please either proposes v0.13.1 or defers to the next `feat`/`fix` PR.
