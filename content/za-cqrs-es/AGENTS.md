# AGENTS.md — MyApp (CQRS + Event Sourcing)

> For AI coding agents (Claude Code, Cursor, GitHub Copilot, Codex, Aider, …) working on this codebase.

This is a CQRS + Event Sourcing Web API scaffolded from `dotnet new za-cqrs-es`. Built on the ZeroAlloc.* ecosystem (source-generated, AOT-safe, zero-allocation packages).

## 1. Project shape

```
HTTP POST /orders
        │
        ▼
┌─────────────────────────┐
│ MyApp.Api               │  Endpoint → maps DTO → IMediator.Send
└─────────┬───────────────┘
          │
          ▼
┌─────────────────────────┐
│ MyApp.Application       │  Commands + Handlers + Projections (INotificationHandler<TEvent>)
└─────────┬───────────────┘
          │
          ▼
┌─────────────────────────┐    ┌──────────────────────────────────┐
│ MyApp.Domain            │ ◀──│ MyApp.Infrastructure             │
│  Aggregates, Events,    │    │  IEventStore + EventStoreMediator│
│  Value Objects, FSMs    │    │  Bridge + ZA.ORM projection repos│
└─────────────────────────┘    └──────────────────────────────────┘
```

- **Domain** — aggregates (`Aggregate<TId, TState>`), events, state-machine FSMs (`[StateMachine]`), value objects.
- **Application** — command/handler slices, projection handlers (`INotificationHandler<TEvent>`).
- **Infrastructure** — event store wiring, EventStoreMediatorBridge, ZA.ORM projection repos.
- **Api** — Minimal API endpoints, DTOs, JWT auth, OpenTelemetry composition.

## 2. How to add a write slice

1. Add aggregate command method (raises an event).
2. Add `<Event>` record in `Domain/<Aggregate>/Events/`. Mark with `[INotification]` if the projection bridge should fan it out.
3. Add the `Apply(<Event> e)` partial method on the state struct.
4. Wire ApplyEvent's switch arm on the aggregate.
5. Add command + handler in `Application/<Aggregate>/<Verb>/`.
6. Add endpoint in `Api/Endpoints/`.

## 3. How to add a projection

1. Implement `INotificationHandler<TEvent>` in `Application/Projections/`.
2. Add `[Command]` upsert partial on the projection repository in `Infrastructure/Projections/`.
3. Add migration for the read-table under `Infrastructure/Persistence/Migrations/Sqlite/NNN_<name>.sql`.
4. Register the projection handler in `ApplicationServiceCollectionExtensions`.
