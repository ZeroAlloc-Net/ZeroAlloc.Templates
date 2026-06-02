# AGENTS.md — MyApp

> For AI coding agents (Claude Code, Cursor, GitHub Copilot, Codex, Aider, …) working on this codebase.

This is a Clean Architecture Web API scaffolded from `dotnet new za-clean`. It uses the ZeroAlloc.* ecosystem (source-generated, AOT-safe, zero-allocation packages). For human-facing docs, see [docs/za-clean.md](docs/za-clean.md). For the design decisions behind the layout, see [the template's design doc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-clean.md).

## 1. Project shape

Four projects under `src/`, three under `tests/`, two under `benchmarks/`. Dependency direction is strictly **inward**:

```
HTTP POST /orders
        │
        ▼
┌─────────────────────────┐
│ MyApp.Api               │  Endpoint → maps DTO → IMediator.Send
│  Endpoints/, Mappings/  │  ASP.NET Core + JWT auth + OpenTelemetry
└─────────┬───────────────┘
          │ depends on
          ▼
┌─────────────────────────┐
│ MyApp.Application       │  Commands, Queries, Handlers, Validators
│  CreateOrder/, GetById/ │  ZA.Mediator, ZA.Mapping, ZA.Inject
└─────────┬───────────────┘
          │ depends on
          ▼
┌─────────────────────────┐    ┌──────────────────────────────────┐
│ MyApp.Domain            │ ◀──│ MyApp.Infrastructure             │
│  Order, Money, OrderId  │    │  ZA.ORM repos (IAsyncDbConnection)│
│  ZA.ValueObjects, ZA.Results│    │  ZA.Rest, ZA.Resilience, ZA.Inject│
└─────────────────────────┘    └──────────────────────────────────┘
```

- **Domain** — entities, value objects. No persistence, no ASP.NET. Pure invariants.
- **Application** — CQRS slice. Handlers return `ValueTask<Result<T, ApplicationError>>`.
- **Infrastructure** — ZA.ORM partial repositories over `IAsyncDbConnection` (raw `Microsoft.Data.Sqlite` / `Npgsql` providers), embedded SQL migrations via `MigrationRunner`, outbound HTTP via ZA.Rest, resilience policies.
- **Api** — Minimal API endpoints, DTOs, JWT auth, OpenTelemetry composition.

## 2. Boundary rules (enforced)

`tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` enforces these via NetArchTest. Violations fail CI.

- Domain references nothing outside Domain (no persistence, no ASP.NET, no Application/Infra/Api).
- Application references only Domain (plus ZA packages).
- Infrastructure references Domain + Application (plus ADO.NET providers + ZA).
- Api references everything (it's the composition root).
- Handlers (`IRequestHandler<,>`) live only in Application.
- ZA.ORM `[Query]`/`[Command]` partial repositories live only in Infrastructure. They depend on `System.Data.Async.IAsyncDbConnection`, which is injected per request via the scoped DI registration in `InfrastructureServiceCollectionExtensions`.

**Before adding a `using` across layers, ask: "does the dependency rules table allow this?"** If not, the code belongs in a different layer.

## 3. How to add things

### Add a command + handler

1. Create `src/MyApp.Application/<Feature>/<Cmd>Command.cs` — record implementing `IRequest<Result<T, ApplicationError>>`.
2. Create `src/MyApp.Application/<Feature>/<Cmd>Handler.cs` — `[Scoped]` class implementing `IRequestHandler<<Cmd>Command, Result<T, ApplicationError>>`. Returns `ValueTask<…>`, **not** `Task<…>`.
3. If validation is needed: extend the hand-rolled `CreateOrderValidator` pattern OR create a parallel validator class. Call from the top of `Handle`.
4. ZA.Inject auto-registers via `[Scoped]` — no DI changes needed.

### Add a query

Same as command but `IRequest<Result<TResponse, ApplicationError>>` typically with a smaller response shape.

### Add an endpoint

1. Add a DTO record in `src/MyApp.Api/Dtos/`.
2. Add a `[Map<<RequestDto>, <Command>>]` partial class in `src/MyApp.Api/Mappings/` (or hand-roll if the mapping isn't trivial — see `OrderToResponse.cs` for an example).
3. Add the endpoint in `src/MyApp.Api/Endpoints/<Feature>Endpoints.cs`. Wire `IMediator.Send` + map result → `Results.Ok` / `Results.Created` / `Results.Problem`.
4. Register the policy via `.RequireAuthorization("OrdersRead")` or `"OrdersWrite"` (or add a new policy in `Program.cs`).

### Add a value object

1. Create `src/MyApp.Domain/ValueObjects/<Name>.cs` — `readonly partial struct` with `[ValueObject]` attribute from ZA.ValueObjects.
2. Add a public `Amount`/`Value` property + private ctor + static `TryCreate(...) → Result<<Name>, string>` factory.
3. **Storage round-trip:** if the value object appears in a stored column, add a `<Name>Converter` static helper alongside `MoneyConverter.cs` exposing `ToStorage(<Name>) -> string` and `FromStorage(string) -> <Name>`. The repository's `[Command]` partial takes the converted `string` parameter; the read path materialises the row record and the handler (or repository, on `GetByIdAsync`) calls `FromStorage(...)` to rehydrate the value object. See `OrderRepository.cs:14-22` (the `AddAsync` body) for the reference shape — `MoneyConverter.ToStorage(order.Total)` is passed straight into the generator-emitted `InsertOrderAsync` partial.

### Add a validation rule

ZA.Validation's `[Validate]` source generator is wired up — `CreateOrderCommand` already uses it. To add or change a rule:

1. Edit the `[property: …]` attribute on the relevant property of `src/MyApp.Application/<Feature>/<Cmd>Command.cs`. Examples: `[NotEmpty]`, `[GreaterThan(0)]`, `[Matches(regex)]`, `[LessThan(N)]`, `[MaxLength(N)]`.
2. Build — the generator regenerates `<Cmd>CommandValidator` automatically. No other wiring change needed.
3. Add a unit test in `tests/MyApp.UnitTests/Application/` that asserts the new rule fires.

The thin wrapper at `CreateOrderValidator.cs` exists for two reasons: it caches the generator-emitted validator instance as a static singleton (avoids per-call construction), and maps the generator's `ValidationResult` shape to the `UnitResult<ValidationError>` shape `CreateOrderHandler` consumes. Reuse this wrapper pattern for new commands — copy the file, swap the type name.

## 4. ZA-specific gotchas

These bit us during template construction; they'll bite you too if you don't know them:

| Gotcha | What to do |
|---|---|
| Handlers return `ValueTask<T>`, not `Task<T>` | Match the interface |
| `[Scoped]` / `[Singleton]` / `[Transient]` separate attributes, not `[Service(ServiceLifetime.X)]` | `using ZeroAlloc.Inject;` then `[Scoped]` |
| ZA generators ship as separate `*.Generator` nupkgs | Reference with `<PrivateAssets>all</PrivateAssets>` + `<IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>` |
| ZA.Authorization is host-agnostic abstractions only | Use vanilla `AddAuthorizationBuilder().AddPolicy(...).RequireClaim(...)` |
| ZA.Telemetry is a code-gen instrumentation library | Use vanilla OpenTelemetry; opt into `[Instrument]` per method |
| ZA.Mapping needs `<PrivateAssets>all</PrivateAssets>` to prevent ZAMP006 across assembly boundaries | Set on the `<PackageReference>` in Application + Api csprojs |
| `OrderId` / `CustomerId` use `[ValueObject]` from ZA.ValueObjects (equality only, no factory) | Hand-write `TryCreate` if validation is needed |
| Switching a request to `IAuthorizedRequest<TPayload>` for Result-style auth | Under AOT publish, the deny path silently throws `AuthorizationDeniedException` instead of returning `Result<T, AuthorizationFailure>.Failure(...)`. Add a `[ModuleInitializer]` carrier method with `[DynamicDependency(PublicMethods, typeof(Result<TPayload, AuthorizationFailure>))]` per `TPayload` you use. See [ZA.Mediator.Authorization AOT docs](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/authorization.md#aot-publish). |

## 5. AOT publish

The Api layer enables `<PublishAot>true</PublishAot>` and CI's
`aot-publish-smoke` job verifies `dotnet publish -p:PublishAot=true -r linux-x64`
produces a working binary that boots and responds to `/healthz`.

The ZA.ORM swap eliminated the previous AOT blockers:
- No EF Core compiled-model dance (no DbContext at all).
- No design-time model pipeline (`Database.MigrateAsync` / `EnsureCreated` were
  the reflection-anchored APIs; `MigrationRunner` is hand-written ADO.NET that
  reads embedded SQL migration resources and tracks state in `__zaorm_migrations`).
- No LINQ-to-SQL precompile-queries collision with co-resident ZA.* source
  generators. The repository's `[Query]`/`[Command]` partials are emitted as
  plain ADO.NET command builds at compile time.

What remains AOT-relevant:
- DTOs that cross the HTTP boundary must be registered in
  `MyApp.Api/JsonContext.cs` via `[JsonSerializable(typeof(...))]`. Forgetting
  surfaces at boot under AOT (the real-run-smoke CI job is the safety net).
- Reflection-based handler scanning (e.g., ZA.Mediator's `RegisterHandlersFromAssembly`)
  gets trimmed under AOT, so `ApplicationServiceCollectionExtensions` registers
  handlers manually (`services.AddScoped<IRequestHandler<TReq, TResp>, ConcreteHandler>()`
  per handler).
- Trim-warnings from OpenTelemetry / Npgsql / SQLite are gated under
  `<TrimmerSingleWarn>true</TrimmerSingleWarn>` — they're invoked outside the
  hot path or guarded at runtime.

## 6. How to verify

```bash
# Build the whole solution
dotnet build MyApp.slnx

# Run all tests — unit + architecture + integration
dotnet test MyApp.slnx

# Skip slow categories during inner-loop dev
dotnet test --filter "Category!=Slow"

# Run the BDN write-pipeline benchmark
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# Run the NBomber load test (two terminals)
dotnet run --project src/MyApp.Api                              # terminal 1
dotnet run -c Release --project benchmarks/MyApp.LoadTest       # terminal 2
```

Pre-commit checklist:
- `dotnet build` is 0 errors, 0 warnings
- All architecture tests pass (`dotnet test tests/MyApp.ArchitectureTests`)
- Any new code path has a test
- Conventional commit message (`feat(application): ...`, `fix(api): ...`, etc.)
