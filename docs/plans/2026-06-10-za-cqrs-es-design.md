# `za-cqrs-es` Template — Design

**Status:** approved 2026-06-10; **paused 2026-06-10 after Task 1 first attempt surfaced 3 upstream gaps** — see Implementation plan's "Upstream prerequisites" section. Resumes once the Outbox package, SQLite event-store adapter, and `[TypedId]` + STJ source-gen integration ship.
**Scope:** New `content/za-cqrs-es/` template in the ZeroAlloc.Templates pack (NOT a separate repo).
**Target version:** ZA.Templates v0.16.0 (minor — adds a new template feature; `feat:` squash).
**Estimated scope:** 5-6 days of focused work, multi-session shipping (plus upstream prerequisite work in separate sessions).
**Branch:** `feat/za-cqrs-es-template` off `main` at post-v0.15.0.

## Background

ZA.Templates currently ships two templates — `za-clean` (Clean Architecture) and `za-vertical-slice`. The cross-repo backlog (`c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md`) has long sketched a third template, `za-cqrs-es`, for CQRS + Event Sourcing. All blocking dependencies are now shipped:

- `ZeroAlloc.EventSourcing` (core + `.Aggregates`)
- `ZeroAlloc.EventSourcing.Sql` (SQLite + Postgres event store backings)
- `ZeroAlloc.EventSourcing.Outbox` (cross-context event dispatch)
- `ZeroAlloc.EventSourcing.Mediator` (v1.0.0, 2026-05-26 — bridge to ZA.Mediator notifications)
- `ZeroAlloc.StateMachine` (aggregate FSM transitions)
- `ZeroAlloc.Mediator` (with `.Authorization` + `.Validation` extensions)

This template demonstrates the *full* CQRS+ES pattern end-to-end with the same Orders + Customers domain the existing templates use, enabling direct architectural-lens comparison.

**Note (2026-06-10):** The "all blocking dependencies are now shipped" claim above was the framing at design time. Task 1's first attempt found three packages either don't exist yet (`ZeroAlloc.EventSourcing.Outbox`, SQLite event-store adapter) or have integration gaps (`[TypedId]` + STJ source-gen). See the implementation plan's "Upstream prerequisites" section for the breakdown. The Outbox + Mediator bridge + StateMachine + Aggregates portions of the dependency list ARE shipped — just not the specific Outbox/SQLite/TypedId integration surfaces this template needs.

## Decision

Adopt **Approach B** from the brainstorm: Orders + Customers parity with the existing templates. Two aggregates, cross-aggregate flow via Outbox to demonstrate the canonical CQRS+ES integration pattern, projection-materialized read side with output caching, fully AOT-clean.

**Rejected alternatives:**

- **Approach A (Bank Account, 1 aggregate, 3 days)** — cleaner pedagogy but breaks line-by-line comparability with the existing templates. Adopters would have to translate concepts twice (lens + domain) to compare.
- **Approach C (in-memory store, no Outbox, 1.5 days)** — promises the template name doesn't deliver. Misleading.
- **Saga inclusion** — explicitly excluded. ZA.Saga is architecturally orthogonal (its own state model via `SagaInstance` rows, consumes events from anywhere). Bundling Saga into za-cqrs-es teaches two patterns at once and muddies both. Filed as a future `za-saga` template — see "Roadmap" below.
- **za-max kitchen sink template** — explicitly rejected. Kitchen-sink templates violate YAGNI, become maintenance-heavy, and teach no single pattern clearly. The composition matrix doc + per-template "Adding ZA.X" recipes serve the ecosystem-completeness goal better.

## What ships in this template

### Folder structure

```
content/za-cqrs-es/
├── README.md
├── AGENTS.md
├── Directory.Packages.props
├── global.json
├── src/
│   ├── MyApp.Domain/
│   │   ├── Orders/
│   │   │   ├── Order.cs                       (Aggregate<OrderId, OrderState>)
│   │   │   ├── OrderState.cs                  (IAggregateState<OrderState>)
│   │   │   ├── OrderFsm.cs                    ([StateMachine] companion)
│   │   │   └── Events/
│   │   │       ├── OrderPlaced.cs             ([OutboxEvent])
│   │   │       ├── OrderShipped.cs            ([OutboxEvent])
│   │   │       └── OrderCancelled.cs
│   │   ├── Customers/
│   │   │   ├── Customer.cs
│   │   │   ├── CustomerState.cs
│   │   │   ├── CustomerFsm.cs
│   │   │   └── Events/
│   │   │       ├── CustomerCreated.cs
│   │   │       ├── LoyaltyPointsCredited.cs
│   │   │       └── CustomerArchived.cs
│   │   └── ValueObjects/
│   │       ├── OrderId.cs                     ([ValueObject] Guid)
│   │       ├── CustomerId.cs                  ([ValueObject] Guid)
│   │       └── Money.cs                       ([ValueObject] — same shape as za-clean)
│   ├── MyApp.Application/
│   │   ├── Orders/
│   │   │   ├── PlaceOrder/                    (PlaceOrderCommand, Handler, Validator)
│   │   │   ├── ShipOrder/
│   │   │   ├── CancelOrder/
│   │   │   └── GetOrder/                      (reads from OrderListingsProjection)
│   │   ├── Customers/
│   │   │   ├── CreateCustomer/
│   │   │   ├── ArchiveCustomer/
│   │   │   ├── GetCustomer/                   (reads from CustomerProfilesProjection)
│   │   │   └── GetCustomerOrders/             (cross-aggregate projection read)
│   │   ├── Projections/
│   │   │   ├── OrderListingsProjection.cs     (INotificationHandler<OrderPlaced/Shipped/Cancelled>)
│   │   │   └── CustomerProfilesProjection.cs  (INotificationHandler<CustomerCreated/LoyaltyPointsCredited/Archived>)
│   │   └── Outbox/
│   │       └── LoyaltyPointsCreditHandler.cs  (Outbox-dispatched handler — cross-aggregate flow)
│   ├── MyApp.Infrastructure/
│   │   ├── EventStore/
│   │   │   ├── EventStoreServiceCollectionExtensions.cs
│   │   │   └── Migrations/
│   │   │       ├── Sqlite/
│   │   │       │   ├── 001_event_store.sql
│   │   │       │   ├── 002_outbox.sql
│   │   │       │   └── 003_projections.sql
│   │   │       └── Postgres/   (same shape)
│   │   └── Projections/
│   │       ├── OrderListingsRepository.cs     (ZA.ORM [Query] partials)
│   │       └── CustomerProfilesRepository.cs
│   └── MyApp.Api/
│       ├── Program.cs
│       ├── Mappings/                          (ZA.Mapping [Map<TSrc, TDst>])
│       └── Endpoints/
│           ├── OrdersEndpoints.cs
│           ├── CustomersEndpoints.cs
│           └── AdminEndpoints.cs              (optional — see stretch section)
├── tests/
│   ├── MyApp.UnitTests/                       (aggregate behavior — given/when/then)
│   ├── MyApp.IntegrationTests/                (HTTP through WebApplicationFactory + in-memory SQLite)
│   └── MyApp.ArchitectureTests/               (NetArchTest dep rules)
└── benchmarks/
    ├── MyApp.Benchmarks/                      (BDN — command dispatch + projection materialization)
    └── MyApp.LoadTest/                        (NBomber against full SUT)
```

### Aggregates

**Order** (`Aggregate<OrderId, OrderState>`):
- Events: `OrderPlaced(OrderId, CustomerId, Money Total)`, `OrderShipped(string Tracking)`, `OrderCancelled(string Reason)`
- StateMachine: `Draft → Placed → Shipped` (terminal), `Draft/Placed → Cancelled` (terminal)
- Commands: `Place(CustomerId, Money)`, `Ship(string tracking)`, `Cancel(string reason)`
- Invariants enforced via `OrderFsm.TryFire(...)` — illegal transitions throw `InvalidOperationException`

**Customer** (`Aggregate<CustomerId, CustomerState>`):
- Events: `CustomerCreated(CustomerId, string Name, string Email)`, `LoyaltyPointsCredited(int Points)`, `CustomerArchived(string Reason)`
- StateMachine: `Active → Archived` (terminal). `LoyaltyPointsCredited` doesn't change FSM state.
- Commands: `Create(string name, string email)`, `CreditLoyaltyPoints(int points)`, `Archive(string reason)`

### Slices (8 core + 2 stretch)

**Writes (5):**
- `POST /orders` → `PlaceOrderCommand`
- `POST /orders/{id}/ship` → `ShipOrderCommand`
- `POST /orders/{id}/cancel` → `CancelOrderCommand`
- `POST /customers` → `CreateCustomerCommand`
- `POST /customers/{id}/archive` → `ArchiveCustomerCommand`

**Reads (3):**
- `GET /orders/{id}` → from `order_listings` projection
- `GET /customers/{id}` → from `customer_profiles` projection
- `GET /customers/{id}/orders` → cross-projection read (orders by customer)

**Stretch (2 admin endpoints — ZA.ORM showcase):**
- `GET /admin/outbox` → list pending outbox entries
- `GET /admin/streams/{aggregateId}` → event history for a given aggregate

The stretch endpoints can ship as a follow-up PR after the core 8 — they're not load-bearing for the CQRS+ES showcase but demonstrate ZA.ORM in two more places.

### ZA package matrix

| Package | Use in template |
|---|---|
| `ZA.EventSourcing` + `.Aggregates` | Aggregate base class, event store contract |
| `ZA.EventSourcing.Sql` | SQLite (default) + Postgres event store backings |
| `ZA.EventSourcing.Outbox` | Cross-aggregate dispatch via `__zaes_outbox` table |
| `ZA.EventSourcing.Mediator` | Projection notification handlers via the mediator bridge |
| `ZA.EventSourcing.Telemetry` | Per-command / per-event spans (transitive) |
| `ZA.Mediator` (+ `.Authorization`, `.Validation`) | Command/query dispatch + pipeline behaviors |
| `ZA.StateMachine` | Aggregate FSM transitions |
| `ZA.Validation` | `[Validate]` on commands |
| `ZA.Results` | `Result<T, ApplicationError>` returns |
| `ZA.ValueObjects` | Typed IDs (`OrderId`, `CustomerId`) + `Money` |
| `ZA.Inject` | Compile-time DI via `[Scoped]` attribute |
| `ZA.Telemetry` | Spans + counters via `[Trace]` / `[Count]` |
| `ZA.Serialisation` | Event-store wire format (`IBufferWriter<byte>`-based) |
| `ZA.ORM` | Projection-table reads (+ optional admin endpoints in stretch) |
| `ZA.Cache` | Output caching on `GET /orders/{id}` + `GET /customers/{id}` (mirrors #197 — proven 100× p99 win) |
| `ZA.Mapping` | Read-model → wire-DTO projection via `[Map<TSrc, TDst>]` |
| `ZA.TestHelpers` | Used across all 3 test projects |

### Storage

- **Event store:** ZA.EventSourcing.Sql, SQLite for `dotnet run` quickstart, Postgres via `Database:Provider=Postgres` config flag (same pattern as the other templates).
- **Projection tables:** same connection, separate tables — `order_listings`, `customer_profiles`, `__zaes_events`, `__zaes_outbox`, `__zaes_outbox_checkpoints`.
- **Migrations:** folder-scoped embedded SQL (`Persistence/Migrations/{Sqlite,Postgres}/NNN_*.sql`) via `MigrationRunner` (same pattern as za-clean).
- **Test fixtures:** in-memory SQLite kept-alive connection, scoped factory override (post-#198 `UseSetting` pattern for config propagation).

## Data flow

### Write path

1. **Endpoint** unmarshals DTO, sends command via `IMediator.Send(...)`.
2. **Validation behavior** runs source-generated validator from `[Validate]`.
3. **Authorization behavior** checks `[RequirePolicy("OrdersWrite")]`.
4. **Command handler** loads aggregate from `IAggregateRepository<Order, OrderId>` (replays events from the store), invokes aggregate method, saves uncommitted events.
5. **Event store append** writes events + projection updates + outbox captures atomically in one DB transaction.
6. **Projections** materialize denormalized read tables via `INotificationHandler<TEvent>`.
7. **Cache invalidation:** endpoint calls `IOutputCacheStore.EvictByTagAsync("orders")` AFTER both event-store append AND projection materialization complete — load-bearing subtlety, see Risk section.
8. Handler returns `Result<TId, ApplicationError>`; endpoint returns `201 Created`.

### Read path

1. **Endpoint** dispatches `GetOrderQuery(orderId)`.
2. **Query handler** calls projection repository (`IOrderListingsRepository.GetByIdAsync(id)` — ZA.ORM `[Query]` partial).
3. Returns flat `OrderReadModel`. ZA.Mapping projects to `OrderResponse` wire DTO.
4. Endpoint returns `200 Ok`.
5. **OutputCache** hit on subsequent calls within TTL — skip Postgres entirely.

### Cross-aggregate flow (the CQRS+ES showcase)

1. `Ship` command → `OrderShipped` event raised (`[OutboxEvent]` attribute).
2. Event written to store + outbox in one tx.
3. **Outbox dispatcher** (hosted service) picks up `OrderShipped`, publishes via ZA.Mediator notification bus.
4. **`LoyaltyPointsCreditHandler`** subscribes — computes `points = (int)orderTotal.Amount`, loads Customer aggregate, invokes `customer.CreditLoyaltyPoints(points)`, saves `LoyaltyPointsCredited` event.
5. **`CustomerProfilesProjection`** picks up `LoyaltyPointsCredited`, updates `customer_profiles.loyalty_points`.
6. Outbox dispatcher marks the row dispatched (idempotent — Customer aggregate refuses duplicate transitions if replayed).

## Testing

**`tests/MyApp.UnitTests`** (~25 tests):
- Pure aggregate behavior, given/when/then style
- One test class per aggregate, covering each command + happy/invalid paths
- Uses `Aggregate.LoadFromHistory(events)` to set up state without an event store

**`tests/MyApp.IntegrationTests`** (~20 tests):
- Full HTTP through `WebApplicationFactory<Program>` + in-memory SQLite kept-alive connection
- One test per endpoint slice (happy path)
- Cache hit/eviction tests (4 facts mirroring za-clean's OutputCacheTests from #197)
- Cross-aggregate flow test: POST OrderShipped → assert `customer_profiles.loyalty_points` updated
- Outbox dispatch test: write an event, assert projection materializes within N retries

**`tests/MyApp.ArchitectureTests`** (~5 tests):
- Domain depends on nothing
- Application depends on Domain only
- Aggregates live in Domain
- Notification handlers (`INotificationHandler<TEvent>`) live in Application
- No EF Core references anywhere

## Benchmarks

**`benchmarks/MyApp.Benchmarks`** — BDN:
- Command dispatch in isolation (mirrors za-clean's `MediatorDispatchBench`)
- Projection materialization per event
- Full-pipeline HTTP write + projection (`WritePipelineBench` equivalent)
- HTTP-level single-request read (`ReadPipelineBench` equivalent)

**`benchmarks/MyApp.LoadTest`** — NBomber:
- `GET /orders/{id}` (cache-hit read path)
- `POST /orders` (write path through full ES stack)

## Roadmap

This template lands in `docs/plans/...` and `content/za-cqrs-es/`. Three related items get queued in the cross-repo `BACKLOG.md`:

1. **`za-saga` template (new entry).** Sibling template demonstrating ZA.Saga orchestration. OrderFulfillment shape from ZA.Saga's existing sample. Consumes events from plain ZA.Mediator publishes (no event store required). Multi-day work, future session.
2. **"Saga over CQRS+ES" composition guide.** A `docs/composition/saga-over-cqrs-es.md` in this template OR in the za-saga template (TBD) showing how an adopter who needs both can wire them together. Documentation, not a third template.
3. **`package-template-matrix.md`** in the ZA.Templates repo docs/. Table showing which ZA package is pre-wired in each template, plus per-row "if you need package X with no template fit, here's how to add it" composition notes. Serves the ecosystem-completeness goal without a kitchen-sink template.

The `package-template-matrix.md` could ship as part of this PR or as a follow-up; doesn't block the template itself.

## Risk

- **Multi-day scope.** Realistically 5-6 days of focused work. First session lands skeleton + one happy-path slice end-to-end + integration test scaffolding; subsequent sessions add remaining slices + projections + outbox + caching + benchmarks. The implementation plan must structure tasks so partial-progress states are mergeable (or at least leave the branch in a buildable state between sessions).
- **Cache invalidation + projection ordering.** Output caching wants to evict AFTER projections materialize, not just after the write succeeds. Otherwise a cached GET returns stale data immediately after a write. The implementation plan needs to make the eviction-after-projection wiring explicit — likely via a final `INotificationHandler<TEvent>` that runs after the projection handlers in registration order.
- **EventSourcing.Sql migration scripts.** Need both Sqlite + Postgres variants of the event-store schema + outbox + projections. ~6 SQL files vs the existing templates' 3-4.
- **AOT compatibility.** All packages used are AOT-clean (verified by their own aot-smoke CI checks). The ZA.EventSourcing.Mediator bridge specifically advertises AOT support. The template's own `aot-publish-smoke` CI check is the regression net.
- **EventStore "load aggregate" cold-replay perf.** Each write rehydrates the aggregate by replaying its events. For high-event-count aggregates this is slow without snapshots. The template will document the snapshot extension point but not ship a snapshot store by default; an FAQ entry in README explains when to add one.
- **Pedagogical confusion vs the existing templates.** Adopters seeing three templates with the same Orders domain might wonder why. README must explicitly frame the choice: "za-clean = Clean Architecture lens, za-vertical-slice = vertical-slice lens, za-cqrs-es = CQRS+ES lens. Same domain, different architectural decisions. Use whichever lens fits your needs."

## Out of scope (explicit)

- **Snapshotting.** Document extension point; no default snapshot store.
- **Replay-from-scratch projection rebuild.** Document the pattern; no `/admin/replay` endpoint.
- **Multi-tenancy.** Out of scope for all current templates.
- **Cross-process Outbox publishers** (message broker, gRPC, etc.). In-process Outbox dispatch only.
- **Saga.** Filed separately as future `za-saga` template.
- **Kitchen-sink template.** No `za-max`; addressed via package-template matrix + composition guides.

## Versioning

- `feat(templates):` squash → ZA.Templates v0.16.0 (minor bump per validated empirical mapping).
- Lands as `content/za-cqrs-es/` folder in ZA.Templates repo, picked up by the existing template-pack csproj automatically.
- Template auto-publishes to NuGet via release-please.yml on the release tag (Templates pattern from earlier in this session).
