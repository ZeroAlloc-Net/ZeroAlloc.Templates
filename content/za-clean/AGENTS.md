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
┌─────────────────────────┐    ┌─────────────────────────┐
│ MyApp.Domain            │ ◀──│ MyApp.Infrastructure    │
│  Order, Money, OrderId  │    │  AppDbContext, EF, Rest │
│  ZA.ValueObjects, ZA.Results│    │  ZA.Resilience, ZA.Inject │
└─────────────────────────┘    └─────────────────────────┘
```

- **Domain** — entities, value objects. No EF, no ASP.NET. Pure invariants.
- **Application** — CQRS slice. Handlers return `ValueTask<Result<T, ApplicationError>>`.
- **Infrastructure** — EF Core SQLite, outbound HTTP via ZA.Rest, resilience policies.
- **Api** — Minimal API endpoints, DTOs, JWT auth, OpenTelemetry composition.

## 2. Boundary rules (enforced)

`tests/MyApp.ArchitectureTests/CleanArchitectureRules.cs` enforces these via NetArchTest. Violations fail CI.

- Domain references nothing outside Domain (no EF, no ASP.NET, no Application/Infra/Api).
- Application references only Domain (plus ZA packages).
- Infrastructure references Domain + Application (plus EF + ZA).
- Api references everything (it's the composition root).
- Handlers (`IRequestHandler<,>`) live only in Application.
- `DbContext` types live only in Infrastructure.

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
3. **EF mapping caveat:** at entity-root level use `b.ComplexProperty(o => o.Total, …)`. At owned-collection level (e.g. inside `OwnsMany`) the `OwnedNavigationBuilder` does NOT expose `ComplexProperty` — use a `ValueConverter<<Name>, string>` round-trip. See `OrderConfiguration.cs:9-26` for the comment + example.

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
| EF Core 9's `OwnedNavigationBuilder` doesn't expose `ComplexProperty` | Use a `ValueConverter` round-trip inside `OwnsMany` |

## 5. AOT-specific gotchas (as of EF Core 10.0.7)

EF Core's NativeAOT support is incomplete in production-ready form. This template
works around the gaps:

| Issue | Workaround in template |
|---|---|
| `Database.MigrateAsync` / `EnsureCreatedAsync` use reflection-based design-time model building | Schema applied via embedded `schema.sql` script (regenerate with `dotnet ef migrations script` after entity changes) |
| EF 10's `--nativeaot` compiled-model emits incorrect `UnsafeAccessorKind.Field` for `readonly struct` ComplexProperty types | `Order.Total` mapped via `HasConversion` (TEXT column `"<amount>\|<currency>"`) instead of `ComplexProperty` |
| LINQ-to-SQL translation requires `--precompile-queries`, blocked by source-gen interactions in our stack | `OrderRepository.GetByIdAsync` written in raw SQL via `db.Database.GetDbConnection().CreateCommand()`; `Order.Materialize` factory exposes hand-hydration to the repository |
| Reflection-based handler scanning (e.g., ZA.Mediator's `RegisterHandlersFromAssembly`) gets trimmed under AOT | `ApplicationServiceCollectionExtensions` registers handlers manually (`services.AddScoped<IRequestHandler<TReq, TResp>, ConcreteHandler>()` per handler) |
| Anonymous types lack source-gen JsonTypeInfo, fail at serialization under AOT | All response shapes are concrete records covered by `JsonContext` |

When EF Core's NativeAOT support matures (compiled-model fix + precompile-queries
fix), revisit these workarounds. Track upstream issues in dotnet/efcore.

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
