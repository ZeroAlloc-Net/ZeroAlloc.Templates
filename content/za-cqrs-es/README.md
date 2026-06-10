# MyApp

CQRS + Event Sourcing Web API. Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem with `PublishAot=true` throughout. Commands flow through an `Order` aggregate (`Aggregate<TId, TState>` from ZA.EventSourcing) that raises events into an event store; projections are `INotificationHandler<TEvent>` handlers materialising denormalized read tables read via ZA.ORM.

## Architecture

```
HTTP POST /orders
        │
        ▼  PlaceOrderCommand (via ZA.Mediator)
┌─────────────────────────┐
│ PlaceOrderHandler       │   constructs Order aggregate, raises OrderPlaced
└─────────┬───────────────┘
          │
          ▼  repo.SaveAsync (ZA.EventSourcing.Aggregates)
┌─────────────────────────┐
│ IEventStore             │   in-memory adapter (Task 5 lands SQL adapter)
└─────────┬───────────────┘
          │  EventStoreMediatorBridge republishes committed events
          ▼
┌─────────────────────────┐
│ OrderListingsProjection │   INotificationHandler<OrderPlaced>
└─────────┬───────────────┘
          │  ZA.ORM
          ▼
┌─────────────────────────┐
│ order_listings table    │   read-side denormalized projection
└─────────────────────────┘
```

## Quickstart

```bash
dotnet run --project src/MyApp.Api
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

## Layout

```
src/
├── MyApp.Domain/            Aggregates (Order), state machines, events, value objects
├── MyApp.Application/       Commands, handlers, projections (INotificationHandler<TEvent>)
├── MyApp.Infrastructure/    Event store wiring + ZA.ORM projection repositories
└── MyApp.Api/               Minimal API endpoints, DTOs, JWT auth, OpenTelemetry

tests/
├── MyApp.UnitTests/         xUnit — aggregate behavior + state machine unit tests
├── MyApp.ArchitectureTests/ NetArchTest — boundary rules + "no EF Core" enforcement
└── MyApp.IntegrationTests/  WebApplicationFactory — end-to-end command→event→projection

benchmarks/
├── MyApp.Benchmarks/        BenchmarkDotNet (fixtures land in Task 7)
└── MyApp.LoadTest/          NBomber (scenario lands in Task 7)
```

## License

MIT — see [LICENSE](LICENSE).
