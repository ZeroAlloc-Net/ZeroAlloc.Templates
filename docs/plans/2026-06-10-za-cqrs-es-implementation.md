# `za-cqrs-es` Template Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement this plan task-by-task.

**Goal:** Build a new `content/za-cqrs-es/` template in the ZA.Templates pack — a CQRS + Event Sourcing reference using the same Orders + Customers domain as the existing templates.

**Architecture:** Mirror za-clean's Clean-Architecture flavor (Domain / Application / Infrastructure / Api). Two aggregates (Order, Customer) via `Aggregate<TId, TState>`. SQLite event store (via the yet-to-be-shipped SQLite adapter — see Upstream prerequisites below). Projections as `INotificationHandler<TEvent>` materializing denormalized read tables read via ZA.ORM, fanned out via direct `IMediator.Publish` post-`SaveAsync` (not the `EventStoreMediatorBridge`, which is a known-stream subscription model — wrong fit for per-aggregate fan-out). Cross-aggregate flow via the Outbox package (also yet-to-ship). Output caching on GET endpoints with eviction wired AFTER projection materialization.

**Tech Stack:** .NET 10, ZA.EventSourcing (core + .Aggregates + .Sql + .Outbox + .Mediator + .Telemetry), ZA.Mediator (.Authorization, .Validation), ZA.StateMachine, ZA.Validation, ZA.Results, ZA.ValueObjects, ZA.Inject, ZA.Telemetry, ZA.Serialisation, ZA.ORM, ZA.Cache, ZA.Mapping, ZA.TestHelpers, xUnit, BenchmarkDotNet, NBomber, NetArchTest. PublishAot=true throughout.

**Design reference:** [docs/plans/2026-06-10-za-cqrs-es-design.md](2026-06-10-za-cqrs-es-design.md) (commit `15d83f2`)
**Branch:** `feat/za-cqrs-es-template` off `main` at post-v0.15.0.

---

## Upstream prerequisites (resolved 2026-06-11 — Task 1 redo unblocked)

Task 1's first attempt (commit `fef3f2f`) surfaced three upstream gaps in the ZA package set. As of 2026-06-11 all three have been addressed in their respective repos; the Task 1 redo subsection at the bottom of this section describes the mechanical workaround flip.

1. ✅ **`ZeroAlloc.EventSourcing.Outbox v0.1.0`** shipped via `ZeroAlloc.EventSourcing` PR #185. `[OutboxEvent]` attribute + dispatcher are available. Required by Task 5 (cross-aggregate flow); Task 1 still uses direct `IMediator.Publish` post-`SaveAsync` because synchronous in-process projections do not need Outbox.
2. ✅ **`ZeroAlloc.EventSourcing.Sqlite v0.1.0`** shipped via `ZeroAlloc.EventSourcing` PR #187 + #192 (`*` global stream consistency across all 4 adapters). The SQLite `IEventStore` exists; the Task 1 redo swaps `.UseInMemoryEventStore()` → SQLite.
3. ✅ **`ZeroAlloc.ValueObjects [TypedId]` cross-assembly converter.** Shipped in `ZeroAlloc.ValueObjects 1.7.1` (PR #53 — `fix(gen): emit public JsonConverter`). The converter is now `public sealed`, instantiable cross-assembly.
   - ⚠️ **Important correction to the original gap framing.** The shipped fix does NOT enable automatic STJ source-gen discovery of `[JsonConverter]`. Roslyn does not propagate cross-generator output to STJ's source generator, so a `[JsonSerializable(typeof(OrderId))]` declaration alone silently produces POCO serialization (`{}` output, not the converter-driven string form). The working pattern is **explicit converter registration**: `options.Converters.Add(new OrderId.TypedIdJsonConverter())` plus `[JsonSerializable(typeof(OrderId))]` plus `options.TypeInfoResolver = AppJsonContext.Default`. See [ZeroAlloc.ValueObjects/docs/typed-id/json.md#source-gen-contexts-jsonserializercontext](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects/blob/main/docs/typed-id/json.md#source-gen-contexts-jsonserializercontext) and the postmortem in `ZeroAlloc.ValueObjects/docs/plans/2026-06-11-typedid-stj-sourcegen-design.md` for the full story.

Two implementer-side claims that were not real gaps:
- ❌ **"Guid doesn't expose `ToString(InvariantCulture)`"** — false. The proper attribute for Guid-backed IDs is `[TypedId]` not `[ValueObject]`; the dedicated `TypedIdGuidWriter` emits `Value.ToString("D", CultureInfo.InvariantCulture)` which compiles fine. The STJ source-gen integration was the real gap (see above).
- ❌ **"`EventStoreMediatorBridge` single-stream is a bug"** — by design, not a bug. The bridge is for known-stream subscriptions, not per-aggregate fan-out. For per-aggregate stream topologies (`order-{guid}`), direct `IMediator.Publish(event, ct)` after `repo.SaveAsync()` is the canonical projection wiring — what Task 1 ships and what later tasks build on.

### Task 1 redo (post-upstream-merge)

With `ZeroAlloc.ValueObjects 1.7.1` released to NuGet (2026-06-11), the Task 1 redo is a focused PR with the following diff:

1. **Bump `content/za-cqrs-es/Directory.Packages.props`**: `ZeroAlloc.ValueObjects 1.7.0` → `1.7.1`. The existing pin is one version short of the fix.
2. **`content/za-cqrs-es/src/MyApp.Domain/ValueObjects/OrderId.cs`** — flip workaround to `[TypedId]`:
   ```csharp
   [TypedId(Strategy = IdStrategy.Uuid7)]
   public readonly partial record struct OrderId;
   ```
   Drop the hand-rolled `New()` factory (generator emits it). Remove the workaround `<remarks>` block.
3. **`content/za-cqrs-es/src/MyApp.Domain/ValueObjects/CustomerId.cs`** — same flip.
4. **`content/za-cqrs-es/src/MyApp.Api/Program.cs:94-97`** — register converters explicitly before the resolver chain insert:
   ```csharp
   builder.Services.ConfigureHttpJsonOptions(o =>
   {
       o.SerializerOptions.Converters.Add(new OrderId.TypedIdJsonConverter());
       o.SerializerOptions.Converters.Add(new CustomerId.TypedIdJsonConverter());
       o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
   });
   ```
   This is the **load-bearing line** — without it, STJ source-gen silently serializes OrderId/CustomerId as `{}`.
5. **`content/za-cqrs-es/src/MyApp.Infrastructure/EventStore/MyAppEventSerializer.cs`** — events flowing through the event-store payload path have the same registration requirement. Refactor to use a shared `JsonSerializerOptions`:
   ```csharp
   private static readonly JsonSerializerOptions _options = new()
   {
       Converters =
       {
           new MyApp.Domain.ValueObjects.OrderId.TypedIdJsonConverter(),
           new MyApp.Domain.ValueObjects.CustomerId.TypedIdJsonConverter(),
       },
       TypeInfoResolver = MyAppEventJsonContext.Default,
   };
   // Then: JsonSerializer.SerializeToUtf8Bytes(p, typeof(OrderPlaced), _options)
   ```
   The `MyAppEventJsonContext` partial class declarations stay as-is.
6. **`content/za-cqrs-es/src/MyApp.Infrastructure/InfrastructureServiceCollectionExtensions.cs:54`** — swap event store:
   ```csharp
   services.AddEventSourcing()
           .UseSqliteEventStore(connectionString)   // was: .UseInMemoryEventStore()
           .UseAggregateRepository<Order, OrderId>(...)
   ```
   Verify the actual API surface against the `ZeroAlloc.EventSourcing.Sqlite v0.1.0` package — extension method name and `connectionString` parameter shape may differ.
7. **`content/za-cqrs-es/tests/MyApp.IntegrationTests/PlaceOrderEndpointTests.cs`** — JSON shape changes from `{"Value":"guid"}` (record-struct natural shape) to bare `"hex-guid"` (TypedId converter shape). Any assertion that pins the envelope shape needs updating. Side-effect assertions (status codes, projection row lookups) are unaffected.

**Commit shape:** single PR squashed as `chore(za-cqrs-es): adopt [TypedId] + SQLite event store now that upstream gaps shipped` — patch-equivalent, no version bump (per the validated empirical release-please mapping that `chore:` produces no bump).

**Verification:** integration test suite green, `aot-publish-smoke-cqrs-es` CI job green, manual `dotnet new za-cqrs-es && dotnet run` smoke against `POST /orders` returns a hex-string ID (not the `{"Value":...}` envelope).

**Not in this PR:** the brief's "fixup in place" option is OFF — `fef3f2f` was merged via #189 already, so we land the redo as a new commit on `feat/za-cqrs-es-template` rather than rewriting history.

---

## Shipping pattern (read this before starting any task)

Each task is its own PR. Most tasks use **`chore(za-cqrs-es):`** as the squash subject — no version bump per the validated empirical mapping. The FINAL Task 8 squash uses **`feat(templates):`** so release-please cuts a single v0.16.0 minor capturing the whole template feature.

Why: avoids 7 intermediate patch bumps + lets the release notes summarize "new za-cqrs-es template" rather than 8 incremental commits.

Per-task rhythm:
1. New short-lived branch off `main` (after each merge, sync main + branch from there)
2. Implement
3. Build + tests pass locally (SDK pin dance if needed — relax `global.json` to `10.0.100` / `latestFeature`, **never commit the relax**)
4. Commit with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer
5. Push + open PR + watch CI (all 6 checks green)
6. Admin-squash-merge with `chore(za-cqrs-es):` subject
7. Sync main; next task

The current branch `feat/za-cqrs-es-template` already has the design + this plan committed. Task 1 starts the implementation series.

---

## Conventions (apply to every task)

- **Co-Authored-By trailer:** `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` (verify via `git log -5 --format=%B`)
- **AOT-clean:** every commit must pass the `aot-publish-smoke-cqrs-es` CI check once Task 1 lands it
- **SDK pin:** relax `global.json` to `10.0.100` only when needed locally; restore via `git restore global.json` before staging
- **No EF Core references** — this is a ZA.ORM template
- **Mirror existing templates** — when in doubt, look at `content/za-clean/` first, then `content/za-vertical-slice/`. The existing two are the reference for project shapes, csproj configurations, naming, and conventions

---

## Task 1 — Skeleton + first happy-path slice

**Goal:** Create the `content/za-cqrs-es/` directory with all source/test/bench projects, integrate into the template-pack csproj (auto via `content/**/*` glob — no edit needed) and CI (`build-cqrs-es` job + `aot-publish-smoke-cqrs-es` job). Ship ONE complete slice end-to-end: `POST /orders` → PlaceOrder → OrderPlaced event → store append → `OrderListingsProjection` materializes → integration test asserting the full flow.

This task is **the largest by file count** (skeleton creation). Subsequent tasks add slices on top.

### Files to create

```
content/za-cqrs-es/
├── README.md                               (~30 lines, references the architectural-lens framing)
├── AGENTS.md                               (mirrors za-clean's AGENTS.md shape; CQRS+ES recipe)
├── Directory.Packages.props                (mirrors za-clean — add ZA.EventSourcing.* + ZA.StateMachine packages)
├── global.json                             (10.0.300 / latestMinor — same pin as siblings)
├── MyApp.slnx                              (solution file referencing all 9 projects)
├── .template.config/
│   └── template.json                       (registers `dotnet new za-cqrs-es`)
└── src/
    ├── MyApp.Domain/
    │   ├── MyApp.Domain.csproj
    │   ├── Orders/
    │   │   ├── Order.cs                    (Aggregate<OrderId, OrderState>)
    │   │   ├── OrderState.cs               (IAggregateState<OrderState>)
    │   │   ├── OrderFsm.cs                 ([StateMachine] companion)
    │   │   └── Events/OrderPlaced.cs       ([OutboxEvent])
    │   └── ValueObjects/
    │       ├── OrderId.cs                  ([ValueObject] Guid)
    │       ├── CustomerId.cs               ([ValueObject] Guid)
    │       └── Money.cs                    ([ValueObject] — copy from za-clean)
    ├── MyApp.Application/
    │   ├── MyApp.Application.csproj
    │   ├── ApplicationServiceCollectionExtensions.cs
    │   ├── Orders/PlaceOrder/
    │   │   ├── PlaceOrderCommand.cs        ([Validate] [RequirePolicy("OrdersWrite")])
    │   │   └── PlaceOrderHandler.cs        (IRequestHandler<...>)
    │   ├── Projections/
    │   │   └── OrderListingsProjection.cs  (INotificationHandler<OrderPlaced>)
    │   └── IOrderListingsRepository.cs
    ├── MyApp.Infrastructure/
    │   ├── MyApp.Infrastructure.csproj
    │   ├── InfrastructureServiceCollectionExtensions.cs (AddMyAppInfrastructure)
    │   ├── EventStore/
    │   │   └── Migrations/
    │   │       └── Sqlite/
    │   │           ├── 001_event_store.sql       (table __zaes_events + __zaes_streams)
    │   │           ├── 002_outbox.sql            (table __zaes_outbox + checkpoints)
    │   │           └── 003_order_listings.sql    (read-side projection table)
    │   └── Projections/
    │       └── OrderListingsRepository.cs        (ZA.ORM [Query] partials)
    └── MyApp.Api/
        ├── MyApp.Api.csproj                       (PublishAot=true)
        ├── Program.cs                             (composes ES + Mediator + cache + endpoints)
        ├── appsettings.json
        ├── appsettings.Development.json
        ├── Dtos/PlaceOrderRequest.cs
        └── Endpoints/OrdersEndpoints.cs           (POST /orders only for now)

tests/
├── MyApp.UnitTests/MyApp.UnitTests.csproj
├── MyApp.UnitTests/Domain/Orders/OrderTests.cs
├── MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
├── MyApp.IntegrationTests/MyAppFactory.cs        (use UseSetting pattern from #198)
├── MyApp.IntegrationTests/TestJwt.cs
├── MyApp.IntegrationTests/PlaceOrderEndpointTests.cs
├── MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj
└── MyApp.ArchitectureTests/CleanArchitectureRules.cs

benchmarks/
├── MyApp.Benchmarks/MyApp.Benchmarks.csproj      (empty shell — fixtures land in Task 7)
└── MyApp.LoadTest/MyApp.LoadTest.csproj          (empty shell — scenario lands in Task 7)
```

### Steps

**Step 1 — bootstrap directory + Directory.Packages.props + global.json + slnx**

Mirror `content/za-clean/`:

```powershell
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
mkdir content/za-cqrs-es
Copy-Item content/za-clean/global.json content/za-cqrs-es/
Copy-Item content/za-clean/Directory.Packages.props content/za-cqrs-es/
```

Read `content/za-clean/Directory.Packages.props` and add the ES-specific package versions (look up current versions on NuGet):
- `ZeroAlloc.EventSourcing` (+ `.Aggregates`, `.Sql`, `.Outbox`, `.Mediator`, `.Telemetry`)
- `ZeroAlloc.StateMachine`

Create `content/za-cqrs-es/MyApp.slnx` by copying `content/za-clean/MyApp.slnx` and editing the relative project paths.

**Step 2 — `.template.config/template.json`**

Copy from `content/za-clean/.template.config/template.json`. Update:
- `name` → `"ZeroAlloc CQRS+ES Web API"`
- `shortName` → `"za-cqrs-es"`
- `identity` / `groupIdentity` → `"ZeroAlloc.Templates.Cqrs.Es"`
- `defaultName` → `"MyApp"`
- `description` → `"CQRS + Event Sourcing Web API template — aggregates, event store, projections, output caching."`

**Step 3 — Domain project (OrderId, CustomerId, Money, OrderState, OrderFsm, Order, OrderPlaced event)**

`MyApp.Domain.csproj` mirrors za-clean's Domain csproj. Reference packages:
- `ZeroAlloc.EventSourcing` (for `Aggregate<TId, TState>` + `IAggregateState<TState>`)
- `ZeroAlloc.StateMachine` (for `[StateMachine]` + `[Transition]`)
- `ZeroAlloc.ValueObjects` (for `[ValueObject]`)
- `ZeroAlloc.ValueObjects.Generator` (analyzer)

Value objects mirror za-clean exactly. Use `Guid`-backed `OrderId`/`CustomerId` (matches the ES sample at `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.EventSourcing/samples/ZeroAlloc.EventSourcing.AotSmoke/Domain.cs:9`).

`OrderState.cs` — partial struct implementing `IAggregateState<OrderState>`:
```csharp
public partial struct OrderState : IAggregateState<OrderState>
{
    public static OrderState Initial => default;
    public OrderStatus Status { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public decimal Total { get; private set; }
    public string Currency { get; private set; }

    internal OrderState Apply(OrderPlaced e) =>
        this with { Status = OrderStatus.Placed, CustomerId = e.CustomerId, Total = e.Total, Currency = e.Currency };
}

public enum OrderStatus { Draft, Placed, Shipped, Cancelled }
```

`OrderFsm.cs` — copy the shape from the ES sample's `OrderFsm`:
```csharp
public enum OrderTrigger { Place, Ship, Cancel }

#pragma warning disable ZSM0003
[StateMachine(InitialState = nameof(OrderStatus.Draft))]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Draft,  On = OrderTrigger.Place,  To = OrderStatus.Placed)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Placed, On = OrderTrigger.Ship,   To = OrderStatus.Shipped)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Draft,  On = OrderTrigger.Cancel, To = OrderStatus.Cancelled)]
[Transition<OrderStatus, OrderTrigger>(From = OrderStatus.Placed, On = OrderTrigger.Cancel, To = OrderStatus.Cancelled)]
[Terminal<OrderStatus>(State = OrderStatus.Shipped)]
[Terminal<OrderStatus>(State = OrderStatus.Cancelled)]
public sealed partial class OrderFsm
{
    public OrderFsm(OrderStatus current) => _state = current;
}
#pragma warning restore ZSM0003
```

`Order.cs` — Aggregate base class:
```csharp
public sealed partial class Order : Aggregate<OrderId, OrderState>
{
    public void Place(CustomerId customerId, decimal total, string currency)
    {
        var fsm = new OrderFsm(State.Status);
        if (!fsm.TryFire(OrderTrigger.Place))
            throw new InvalidOperationException($"Cannot place order in status {State.Status}.");
        Raise(new OrderPlaced(Id, customerId, total, currency));
    }

    protected override OrderState ApplyEvent(OrderState state, object @event) => @event switch
    {
        OrderPlaced p => state.Apply(p),
        _ => state,
    };
}
```

`OrderPlaced.cs` — event record, marked `[OutboxEvent]`:
```csharp
[OutboxEvent]
public record OrderPlaced(OrderId OrderId, CustomerId CustomerId, decimal Total, string Currency);
```

**Step 4 — Application project (`PlaceOrderCommand`, `PlaceOrderHandler`, `OrderListingsProjection`, `IOrderListingsRepository`)**

Mirror za-clean's Application project shape (one folder per feature). `PlaceOrderCommand` is a sealed record with `[Validate]` + `[RequirePolicy("OrdersWrite")]`:
```csharp
[Validate]
[RequirePolicy("OrdersWrite")]
public sealed record PlaceOrderCommand(
    [property: NotEqual(Guid.Empty)] CustomerId CustomerId,
    [property: GreaterThan(0)] decimal Total,
    [property: NotEmpty] string Currency)
    : IRequest<Result<OrderId, ApplicationError>>;
```

`PlaceOrderHandler` injects `IAggregateRepository<Order, OrderId>` from ZA.EventSourcing. The handler:
- Creates a new `Order` aggregate (assigns `OrderId.New()`)
- Calls `order.Place(...)` (raises OrderPlaced)
- Calls `repo.SaveAsync(order, ct)` — this is what writes to the event store

The `OrderListingsProjection` is an `INotificationHandler<OrderPlaced>` that writes to the `order_listings` table via `IOrderListingsRepository.UpsertAsync(...)`. The repo uses ZA.ORM `[Command]` for the upsert.

Read the `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.EventSourcing/docs/getting-started/` examples for the canonical wiring shape before writing the handler.

**Step 5 — Infrastructure project (EventStore wiring, SQLite migrations, ZA.ORM repo)**

`InfrastructureServiceCollectionExtensions.AddMyAppInfrastructure(...)` registers:
- `IAsyncDbConnection` (scoped, SQLite or Postgres per `Database:Provider`)
- The event store via ZA.EventSourcing.Sql's `AddSqlEventStore(...)` extension (check ZA.ES.Sql README for exact API)
- The Outbox dispatcher (hosted service)
- `IOrderListingsRepository` → `OrderListingsRepository` (ZA.ORM-generated)

Migration files under `EventStore/Migrations/Sqlite/`:
- `001_event_store.sql` — copy from ZA.EventSourcing.Sql's schema docs
- `002_outbox.sql` — copy from ZA.EventSourcing.Outbox docs
- `003_order_listings.sql` — denormalized read table:
  ```sql
  CREATE TABLE IF NOT EXISTS order_listings (
      id TEXT PRIMARY KEY,
      customer_id TEXT NOT NULL,
      status TEXT NOT NULL,
      total NUMERIC NOT NULL,
      currency TEXT NOT NULL,
      placed_at TEXT NOT NULL
  );
  ```

`OrderListingsRepository.cs` — ZA.ORM partial class with `[Command]` UPSERT and `[Query]` get-by-id partials.

**Step 6 — Api project (Program.cs composition + endpoints)**

`Program.cs` composes everything. Copy from `content/za-clean/src/MyApp.Api/Program.cs` and adapt:
- Replace `AddMyAppApplication(...)` with the za-cqrs-es flavor that registers handlers + projection notification handlers
- Add `AddSqlEventStore(...)` (or equivalent ES wiring) before `var app = builder.Build()`
- Output cache registration (the same custom `CacheAuthenticatedGetsPolicy` from #197 — copy that class into `MyApp.Api/CacheAuthenticatedGetsPolicy.cs`)
- Use `app.UseOutputCache()` after auth + before endpoints

`OrdersEndpoints.MapOrders(app)` with one endpoint:
```csharp
group.MapPost("/", async (PlaceOrderRequest req, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
{
    var cmd = new PlaceOrderCommand(req.CustomerId, req.Total, req.Currency);
    var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
    if (result.IsSuccess)
    {
        await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);
        return Results.Created($"/orders/{result.Value.Value}", new { id = result.Value.Value });
    }
    return Results.Problem(result.Error.Message, statusCode: 400);
}).RequireAuthorization("OrdersWrite");
```

**Step 7 — Test scaffolding (UnitTests + IntegrationTests + ArchitectureTests)**

`MyApp.UnitTests` — pure aggregate behavior:
```csharp
public class OrderTests
{
    [Fact]
    public void Place_raises_OrderPlaced_event()
    {
        using var order = new Order();
        order.SetId(new OrderId(Guid.NewGuid()));
        order.Place(new CustomerId(Guid.NewGuid()), 99.99m, "EUR");
        Assert.Equal(OrderStatus.Placed, order.State.Status);
        Assert.Equal(99.99m, order.State.Total);
    }
}
```

`MyApp.IntegrationTests/MyAppFactory.cs` — `WebApplicationFactory<Program>` with kept-alive in-memory SQLite + `UseSetting("Database:SchemaStrategy", "Skip")` (per the #198 fix that landed this session). Apply migrations in the factory ctor.

`MyApp.IntegrationTests/PlaceOrderEndpointTests.cs` — one fact:
```csharp
[Fact]
public async Task POST_orders_persists_event_and_materializes_projection()
{
    using var factory = new MyAppFactory();
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.write"]));

    var resp = await client.PostAsJsonAsync("/orders", new { customerId = Guid.NewGuid(), total = 99.99m, currency = "EUR" });

    Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

    // Verify projection materialized
    using var scope = factory.Services.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderListingsRepository>();
    // Pull the row by parsing the Location header for the new ID, then assert
    // status = "Placed" + total = 99.99
}
```

`MyApp.ArchitectureTests/CleanArchitectureRules.cs` — copy from za-clean and adapt project names. Initial rules:
- Domain depends on nothing outside ZA.EventSourcing.Aggregates, ZA.StateMachine, ZA.ValueObjects
- Application depends on Domain only (plus ZA.* packages)
- Infrastructure depends on Application + Domain
- No EF Core references anywhere

**Step 8 — Update CI workflow**

Edit `.github/workflows/ci.yml`. Add two new jobs (or extend an existing matrix):
- `build-cqrs-es` — mirror `build-vs` shape, points at `content/za-cqrs-es/MyApp.slnx`
- `aot-publish-smoke-cqrs-es` — mirror `aot-publish-smoke-vs`, installs `dotnet new za-cqrs-es`, scaffolds, publishes AOT, asserts `/healthz` returns 200

For the AOT smoke check to work, the template's `Program.cs` must include `app.MapGet("/healthz", ...)`. Copy from za-clean.

**Step 9 — Build + test**

```powershell
dotnet build content/za-cqrs-es/MyApp.slnx -c Release -v minimal
dotnet test content/za-cqrs-es/MyApp.slnx --no-build -c Release -v minimal
```

Expected: 0 errors, all tests pass. The IntegrationTests must show the event landing in the store AND the projection materializing.

**Step 10 — Commit**

```powershell
git status
# Stage everything under content/za-cqrs-es/ + the .github/workflows/ci.yml edit
git add content/za-cqrs-es/ .github/workflows/ci.yml
git commit -m @'
chore(za-cqrs-es): skeleton + PlaceOrder slice end-to-end (#189)

Task 1 of the za-cqrs-es template (design at
docs/plans/2026-06-10-za-cqrs-es-design.md, commit 15d83f2).

Lands the full directory skeleton:
- 4 source projects (Domain/Application/Infrastructure/Api)
- 3 test projects (UnitTests/IntegrationTests/ArchitectureTests)
- 2 benchmark shells (BDN + NBomber — fixtures added in Task 7)
- .template.config/template.json registering `dotnet new za-cqrs-es`
- CI jobs build-cqrs-es + aot-publish-smoke-cqrs-es

Plus ONE complete slice end-to-end as the skeleton validation:
- POST /orders → PlaceOrderCommand
- Order aggregate with Place(CustomerId, Total, Currency)
- OrderPlaced event ([OutboxEvent]) written to event store
- OrderListingsProjection materializes order_listings table
- Integration test asserts the full flow

Subsequent tasks add ShipOrder/CancelOrder, the Customer aggregate,
read slices, the cross-aggregate Outbox flow, output caching,
benchmarks, and README/AGENTS.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

**Step 11 — Push + PR + admin-merge**

Open PR titled `chore(za-cqrs-es): skeleton + PlaceOrder slice end-to-end`. Watch CI (8 checks now — the 6 standard + the 2 new cqrs-es-specific). Once green, admin-squash-merge with `chore(za-cqrs-es):` subject. Confirm no release-please bump.

---

## Task 2 — ShipOrder + CancelOrder

**Goal:** Add the remaining two Order write slices. Both are simple aggregate-method-plus-handler-plus-endpoint additions over what Task 1 landed.

**Files:**
- Modify: `src/MyApp.Domain/Orders/Order.cs` (add `Ship(string tracking)` + `Cancel(string reason)` methods)
- Modify: `src/MyApp.Domain/Orders/OrderState.cs` (add `Apply(OrderShipped)` + `Apply(OrderCancelled)`)
- Create: `src/MyApp.Domain/Orders/Events/OrderShipped.cs` (`[OutboxEvent]` — important, this is what triggers Task 5's cross-aggregate flow)
- Create: `src/MyApp.Domain/Orders/Events/OrderCancelled.cs`
- Create: `src/MyApp.Application/Orders/ShipOrder/` (Command + Handler)
- Create: `src/MyApp.Application/Orders/CancelOrder/` (Command + Handler)
- Modify: `src/MyApp.Application/Projections/OrderListingsProjection.cs` (handle the two new events)
- Modify: `src/MyApp.Api/Endpoints/OrdersEndpoints.cs` (`POST /orders/{id}/ship`, `POST /orders/{id}/cancel`)
- Modify: `src/MyApp.Infrastructure/Projections/OrderListingsRepository.cs` (`UpdateStatusAsync` partial)
- Create: `tests/MyApp.UnitTests/Domain/Orders/ShipOrderTests.cs` + `CancelOrderTests.cs`
- Create: `tests/MyApp.IntegrationTests/ShipOrderEndpointTests.cs` + `CancelOrderEndpointTests.cs`

**Steps:** mirror Task 1's pattern. TDD: failing aggregate-behavior test first (`Cannot_ship_a_cancelled_order`), then implementation. Then failing endpoint test, then handler+endpoint implementation.

**Commit subject:** `chore(za-cqrs-es): ShipOrder + CancelOrder slices`

**Verification before commit:**
```powershell
dotnet test content/za-cqrs-es/MyApp.slnx --no-build -c Release -v minimal
```
Expected: all Task 1 tests still green + 4 new aggregate behavior tests + 2 new endpoint integration tests pass.

---

## Task 3 — Customer aggregate + writes

**Goal:** Mirror Task 2's pattern for the second aggregate. CreateCustomer + ArchiveCustomer write slices, customer_profiles projection, customer events.

**Files:**
- Create: `src/MyApp.Domain/Customers/` (Customer.cs, CustomerState.cs, CustomerFsm.cs, Events/CustomerCreated.cs, Events/LoyaltyPointsCredited.cs, Events/CustomerArchived.cs)
- Create: `src/MyApp.Application/Customers/CreateCustomer/` + `ArchiveCustomer/` (Command + Handler)
- Create: `src/MyApp.Application/Projections/CustomerProfilesProjection.cs`
- Create: `src/MyApp.Application/ICustomerProfilesRepository.cs`
- Create: `src/MyApp.Infrastructure/Projections/CustomerProfilesRepository.cs`
- Modify: `src/MyApp.Infrastructure/EventStore/Migrations/Sqlite/004_customer_profiles.sql` (new read table)
- Modify: `src/MyApp.Api/Endpoints/CustomersEndpoints.cs` (new file — `POST /customers`, `POST /customers/{id}/archive`)
- Modify: `src/MyApp.Api/Program.cs` (`app.MapCustomers()`)
- Create unit + integration tests

`LoyaltyPointsCredited` doesn't change `CustomerStatus` (the FSM only handles Active → Archived). The Customer aggregate exposes `CreditLoyaltyPoints(int points)` which Raise()s a `LoyaltyPointsCredited` event but doesn't TryFire the FSM. State.LoyaltyPoints accumulates via `Apply(LoyaltyPointsCredited)`.

**Commit subject:** `chore(za-cqrs-es): Customer aggregate + CreateCustomer + ArchiveCustomer slices`

---

## Task 4 — Reads + projections via ZA.ORM + ZA.Mapping

**Goal:** Three read slices (GetOrder, GetCustomer, GetCustomerOrders). ZA.ORM partials read projection tables. ZA.Mapping projects read models to wire DTOs.

**Files:**
- Create: `src/MyApp.Application/Orders/GetOrder/GetOrderQuery.cs` + `Handler.cs`
- Create: `src/MyApp.Application/Customers/GetCustomer/GetCustomerQuery.cs` + `Handler.cs`
- Create: `src/MyApp.Application/Customers/GetCustomerOrders/GetCustomerOrdersQuery.cs` + `Handler.cs`
- Create: `src/MyApp.Application/Orders/GetOrder/OrderReadModel.cs` (sealed record — flat shape per #173 pattern)
- Create: `src/MyApp.Application/Customers/GetCustomer/CustomerReadModel.cs`
- Modify: `IOrderListingsRepository` + `ICustomerProfilesRepository` (add `GetByIdAsync`, `ListByCustomerAsync`)
- Modify: corresponding repositories with ZA.ORM `[Query]` partials
- Create: `src/MyApp.Api/Dtos/OrderResponse.cs`, `CustomerResponse.cs`, `OrderSummaryResponse.cs`
- Create: `src/MyApp.Api/Mappings/` (ZA.Mapping `[Map<OrderReadModel, OrderResponse>]`-decorated static classes)
- Modify: `OrdersEndpoints.cs` + `CustomersEndpoints.cs` (add GET endpoints)
- Add integration tests for read-after-write (Task 1's PlaceOrder + Task 3's CreateCustomer happy paths)

**Pattern reference:** the #173 flat-read-model refactor in za-clean is the exact shape to mirror for read-side. Use ZA.ORM `[Query]` for the SQL, return `OrderReadModel` from the handler, project via ZA.Mapping at the endpoint boundary.

**Commit subject:** `chore(za-cqrs-es): read slices via ZA.ORM + ZA.Mapping`

---

## Task 5 — Cross-aggregate flow via Outbox

**Goal:** Wire the OrderShipped → LoyaltyPointsCredited cross-aggregate flow via the EventSourcing.Outbox + Mediator bridge. This is the load-bearing CQRS+ES showcase.

**Files:**
- Create: `src/MyApp.Application/Outbox/LoyaltyPointsCreditHandler.cs` (`INotificationHandler<OrderShipped>`)
- Modify: `src/MyApp.Api/Program.cs` (ensure Outbox dispatcher hosted service is registered + the EventSourcing.Mediator bridge wires LoyaltyPointsCreditHandler)
- Create: `tests/MyApp.IntegrationTests/CrossAggregateFlowTests.cs`

The handler logic:
```csharp
public sealed class LoyaltyPointsCreditHandler(
    IAggregateRepository<Customer, CustomerId> repo)
    : INotificationHandler<OrderShipped>
{
    public async ValueTask Handle(OrderShipped @event, CancellationToken ct)
    {
        var customer = await repo.LoadAsync(@event.CustomerId, ct).ConfigureAwait(false);
        var points = (int)Math.Floor(@event.Total);
        customer.CreditLoyaltyPoints(points);
        await repo.SaveAsync(customer, ct).ConfigureAwait(false);
    }
}
```

This handler depends on `OrderShipped` carrying the `CustomerId` — Task 2's `OrderShipped` event already includes it (verify when implementing Task 2).

**Integration test** seeds an Order via POST /orders, ships it via POST /orders/{id}/ship, then asserts `customer_profiles.loyalty_points` was incremented by `(int)Math.Floor(total)`. May need a retry loop with `Task.Delay` since the Outbox dispatcher is async — give it ~5 seconds before failing the test.

**Commit subject:** `chore(za-cqrs-es): cross-aggregate Outbox flow (OrderShipped → LoyaltyPointsCredited)`

---

## Task 6 — Output caching + invalidation ordering

**Goal:** Apply ZA.Cache OutputCache to all 3 read endpoints. Wire eviction AFTER projection materialization (the load-bearing subtlety from the design's Risk section).

**Files:**
- Modify: `src/MyApp.Api/Program.cs` (register two policies: `OrderById` tag `orders`, `CustomerById` tag `customers`)
- Modify: `src/MyApp.Api/Endpoints/OrdersEndpoints.cs` (`.CacheOutput("OrderById")` on GET)
- Modify: `src/MyApp.Api/Endpoints/CustomersEndpoints.cs` (`.CacheOutput("CustomerById")` on GETs)
- Modify: write endpoints to call `IOutputCacheStore.EvictByTagAsync(...)` AFTER `mediator.Send(...)` returns — **but only after projection materialization**. Key subtlety: the projection runs as part of `repo.SaveAsync()`'s transaction; once `SaveAsync` returns success, the projection is durably materialized. So eviction-after-`SaveAsync` is equivalent to eviction-after-projection here. Confirm this assumption against ZA.EventSourcing.Sql's docs — if it uses a deferred dispatch model where projections run on a background thread, eviction needs different wiring (e.g. via a projection-completion event).
- Create: `src/MyApp.Api/CacheAuthenticatedGetsPolicy.cs` — copy from `content/za-clean/src/MyApp.Api/CacheAuthenticatedGetsPolicy.cs` (the custom policy from #197)
- Add OutputCacheTests integration tests mirroring za-clean's pattern (4 facts per entity = 8 total): hit, eviction on write, TTL expiry, per-id isolation

**Commit subject:** `chore(za-cqrs-es): output caching on read endpoints`

---

## Task 7 — Benchmarks + ArchitectureTests rules + stretch admin endpoints

**Goal:** Land BDN + NBomber benchmarks, finalize the architecture-rule tests, and ship the 2 stretch admin endpoints (`GET /admin/outbox`, `GET /admin/streams/{id}`) since they're small and the dependency on the rest of the system is now in place.

**Files (benchmarks):**
- Create: `benchmarks/MyApp.Benchmarks/PlaceOrderBench.cs` — full HTTP pipeline write
- Create: `benchmarks/MyApp.Benchmarks/GetOrderBench.cs` — read path with cache hit/miss
- Create: `benchmarks/MyApp.Benchmarks/MediatorDispatchBench.cs` — in-process aggregate dispatch
- Create: `benchmarks/MyApp.LoadTest/ReadHotPathScenario.cs` — NBomber against GET /orders/{id}
- All mirror the equivalent files in `content/za-clean/benchmarks/`

**Files (admin endpoints — stretch but small):**
- Create: `src/MyApp.Application/Admin/GetOutboxStatus/` (Query + Handler — reads `__zaes_outbox` via ZA.ORM)
- Create: `src/MyApp.Application/Admin/GetEventStream/` (Query + Handler — reads `__zaes_events` for a given aggregate id)
- Create: `src/MyApp.Application/IOutboxAdminRepository.cs` + impl
- Create: `src/MyApp.Application/IEventStreamAdminRepository.cs` + impl
- Modify: `src/MyApp.Api/Endpoints/AdminEndpoints.cs` (new file, `GET /admin/outbox`, `GET /admin/streams/{aggregateId}`)
- Modify: `Program.cs` `app.MapAdmin()`

**Files (architecture):**
- Strengthen `CleanArchitectureRules.cs` to add the `Query_handlers_return_application_query_models_not_domain_entities` rule that landed in za-clean during #173
- Add a new rule: `Notification_handlers_live_in_Application_only` (projections + outbox handlers)

**Commit subject:** `chore(za-cqrs-es): benchmarks + admin endpoints + architecture rules`

---

## Task 8 — README + AGENTS.md + push + PR + admin-merge with feat:

**Goal:** Documentation + final PR ships the template feature with a single minor bump.

**Files:**
- Create: `content/za-cqrs-es/README.md` — cross-template framing ("za-clean = Clean Arch lens, za-vertical-slice = Vertical Slice lens, **za-cqrs-es = CQRS+ES lens** — same domain, different architectural decisions"). Mirror the README shape of za-clean. Document the architectural choices, package matrix, how to scaffold (`dotnet new za-cqrs-es`), how to swap SQLite for Postgres, how to extend with new aggregates / slices.
- Create: `content/za-cqrs-es/AGENTS.md` — mirror za-clean's AGENTS.md. Add CQRS+ES-specific recipes: "Add a new aggregate", "Add a new projection", "Add an outbox-dispatched cross-aggregate handler".
- Modify: top-level `README.md` of ZA.Templates — mention the third template in the "what ships" intro
- Modify: `c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md` — change za-cqrs-es status from "🚧 in design" to "✅ shipped v0.16.0" once this PR's release-please PR merges. Note: workspace BACKLOG.md is local-only docs, not a git repo — separate manual edit, not part of this PR.

**Steps:**
1. Write README + AGENTS docs
2. Run a `dotnet new install ./content/za-cqrs-es` locally, then `dotnet new za-cqrs-es -o /tmp/SmokeTest` in a fresh dir; confirm scaffolds + builds + tests pass
3. Commit
4. Push + open PR titled `feat(templates): za-cqrs-es template — CQRS + Event Sourcing reference (#189)`
5. PR body includes the cross-template framing + a screenshot/dump of the package matrix + the architectural-lens comparison
6. Watch CI — 6 standard checks + the 2 new cqrs-es ones — all green
7. Admin-squash-merge with `feat(templates):` subject so release-please cuts v0.16.0
8. Wait for release-please PR `chore(main): release 0.16.0`; do NOT merge — user handles that

**Commit + PR subject:** `feat(templates): za-cqrs-es template — CQRS + Event Sourcing reference`

---

## Task 9 (optional) — Update workspace BACKLOG.md after v0.16.0 ships

Local-only docs edit (workspace root isn't a git repo). After the user merges the release-please v0.16.0 PR:

- Update `c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md` za-cqrs-es entry: change status from `🚧 in design 2026-06-10` to `✅ shipped v0.16.0 <date>` with a PR link
- Update the template-readiness table accordingly

---

## Acceptance criteria (whole template)

Before declaring the template shippable:

- [ ] `dotnet new install ./content/za-cqrs-es` succeeds
- [ ] `dotnet new za-cqrs-es -o /tmp/Scaffolded` produces a buildable project
- [ ] `dotnet build` on the scaffolded project succeeds with 0 warnings (pre-existing MA0048 file co-location warnings allowed if intentional, per za-clean precedent)
- [ ] `dotnet test` on the scaffolded project — all tests pass
- [ ] `dotnet publish -r linux-x64 -c Release --self-contained` (AOT) succeeds
- [ ] Published binary runs, `/healthz` returns 200, can POST + GET + ship orders via curl
- [ ] CI green on the PR — both `build-cqrs-es` and `aot-publish-smoke-cqrs-es` pass
- [ ] release-please cuts a `chore(main): release 0.16.0` PR

## Out of scope (deferrals)

- Snapshotting — extension point documented, not shipped
- `/admin/replay` projection rebuild endpoint — documented, not shipped
- Multi-tenancy — out of scope
- Postgres provider — config flag honored (Database:Provider=Postgres) but the Postgres migrations folder is empty in initial ship; Postgres support is follow-up work
- Cross-process Outbox publishers (broker, gRPC) — in-process dispatch only

## Risk

- **AOT smoke check might surface unexpected reflection.** ZA.EventSourcing.Sql + ZA.EventSourcing.Mediator both advertise AOT support but the template's specific composition exercises more surface than the individual smoke tests. If `aot-publish-smoke-cqrs-es` fails, investigate the reflection origin — likely either in event store registration (might need `[DynamicallyAccessedMembers]` on aggregate types) or in projection notification handler resolution. Fix at source; do not silence the AOT warnings.
- **EventSourcing.Sql Postgres support.** Verify Postgres migrations during Task 5 — if the migration scripts need significant adaptation, defer Postgres provider to a follow-up.
- **Template-pack csproj globbing.** `<Content Include="content/**/*">` should auto-pick up the new template. If `dotnet pack ZeroAlloc.Templates.csproj` doesn't include the cqrs-es content, investigate the glob — likely just needs to wait for the NuGet pack step to re-run after a clean.
- **CI matrix expansion.** Adding 2 new CI jobs (build-cqrs-es + aot-publish-smoke-cqrs-es) lengthens CI runtime by ~5 min per PR. If this becomes intolerable, consider consolidating into a matrix strategy across all three templates.

## Notes

- **Don't over-engineer the README.** The cross-template framing + package matrix + scaffold/build/test/publish steps + a short "How to extend" section is sufficient. Mirror za-clean's README length.
- **Verify every package version pin** in `Directory.Packages.props` against latest stable on NuGet. ZA.EventSourcing.* might have multiple sub-packages with different version pins; align them where possible.
- **The implementer should run a full SDK pin dance audit** at the start of each task — different local SDK installs may need different relaxations. Always `git restore global.json` before committing.
