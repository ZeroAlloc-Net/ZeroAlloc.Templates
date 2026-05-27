# AGENTS.md — MyApp

> For AI coding agents (Claude Code, Cursor, GitHub Copilot, Codex, Aider, …) working on this codebase.

This is a Vertical Slice Architecture Web API scaffolded from `dotnet new za-vertical-slice`. It uses the ZeroAlloc.* ecosystem (source-generated, AOT-safe, zero-allocation packages). For the design decisions behind the layout, see [the template's design doc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-vertical-slice.md).

## 1. Project shape

One `src/` project. Everything lives inside it, organised by *use case*, not by *layer*. No `Domain` / `Application` / `Infrastructure` / `Api` split.

```
HTTP POST /orders
        │
        ▼
┌──────────────────────────────────────────────────────────┐
│ src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs       │
│                                                           │
│   PlaceOrderCommand : IRequest<Result<OrderId, Error>>   │
│   PlaceOrderValidator : AbstractValidator<PlaceOrderCmd> │
│   PlaceOrderHandler : IRequestHandler<…>                 │
│   PlaceOrderEndpoint.Map(IEndpointRouteBuilder)          │
│   internal sealed class Order  ← persistence entity      │
└────────────────────┬─────────────────────────────────────┘
                     │ depends only on
                     ▼
┌──────────────────────────────────────────────────────────┐
│ src/MyApp/Common/      TypedIds (OrderId, CustomerId),   │
│ src/MyApp/Persistence/ AppDbContext, ZeroAlloc.*         │
│ src/MyApp/Authorization/ [Policy] declarations           │
└──────────────────────────────────────────────────────────┘
```

- **`Features/<Area>/<UseCase>/<UseCase>.cs`** — one slice per use case. Holds the request + validator + handler + endpoint + (optionally) the persistence entity owned by that slice.
- **`Common/`** — primitives shared across slices: TypedIds, the `Error` catalog, telemetry setup.
- **`Persistence/AppDbContext.cs`** — the `DbContext`. Entity definitions live in the slices that *create* the entity (e.g. `PlaceOrder.cs` defines `Order`); `AppDbContext` only exposes the `DbSet<T>`.
- **`Authorization/Policies.cs`** — `[Policy]` declarations used by `[RequirePolicy(...)]` on requests.
- **`Program.cs`** — DI wiring + endpoint-discovery walk (assembly walk that calls `Map` on every `public static class *Endpoint`).

## 2. Convention rules (enforced)

`tests/MyApp.ConventionTests/VerticalSliceConventionRules.cs` enforces these via NetArchTest. Violations fail CI.

- Every type ending in `Command` or `Query` implements `IRequest<>`.
- Every type ending in `Handler` is `public sealed`.
- No slice references another slice's types directly. Slices share via `Common/`, `Persistence/`, or `Authorization/`; cross-slice messaging goes through `IMediator`.

**Before adding a `using MyApp.Features.<OtherSlice>...;`, ask: "is this slice talking to another slice's internals?"** If yes, factor the shared type into `Common/` or send a request through `IMediator`.

## 3. How to add things

### Add a new use case (slice)

1. Create `src/MyApp/Features/<Area>/<UseCase>/<UseCase>.cs`.
2. Define the request as a `readonly record struct *Command`/`*Query` implementing `IRequest<Result<TResponse, Error>>`. Decorate with `[RequirePolicy("...")]` if authentication/authorization applies.
3. Define `*Validator : AbstractValidator<*Command>`.
4. Define `*Handler : IRequestHandler<*Command, Result<TResponse, Error>>`. Return `ValueTask<...>`, **not** `Task<...>`.
5. Define `*Endpoint` as a `public static class` with `public static void Map(IEndpointRouteBuilder)`. Wire `IMediator.Send` + map result to `Results.Created`/`Results.Ok`/`Results.Problem`.
6. If the slice owns a persistence entity (e.g. `PlaceOrder` owns `Order`), define it as `internal sealed class` in the same file. Add the matching `DbSet<T>` to `AppDbContext`.
7. Add a unit test in `tests/MyApp.UnitTests/Features/<Area>/<UseCase>/<UseCase>HandlerTests.cs`.
8. Add an integration test in `tests/MyApp.IntegrationTests/Features/<Area>/<UseCase>/<UseCase>EndpointTests.cs`.

No central registration to edit — `Program.cs` discovers handlers via `RegisterHandlersFromAssembly(...)` and endpoints via the assembly walk.

### Add a value object

1. Create or extend `src/MyApp/Common/ValueObjects.cs` — `readonly partial record struct` with `[TypedId]` attribute from `ZeroAlloc.ValueObjects` (auto-generates `.New()` factory + JSON converter).

### Add a validation rule

ZA.Validation's `[Validate]` source generator is wired up — or you can write `AbstractValidator<T>` directly. Both work. Pick the style that matches the surrounding slice.

### Add a policy

1. Edit `src/MyApp/Authorization/Policies.cs` — declare a new `[Policy("name")]` static class with the policy logic.
2. Decorate the request that needs it with `[RequirePolicy("name")]`.

## 4. ZA-specific gotchas

| Gotcha | What to do |
|---|---|
| Handlers return `ValueTask<T>`, not `Task<T>` | Match the interface |
| `[Scoped]` / `[Singleton]` / `[Transient]` are separate attributes, not `[Service(ServiceLifetime.X)]` | `using ZeroAlloc.Inject;` then `[Scoped]` |
| ZA generators ship as separate `*.Generator` nupkgs | Reference with `<PrivateAssets>all</PrivateAssets>` + `<IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>` |
| ZA.Authorization is host-agnostic abstractions only | Use vanilla `AddAuthorizationBuilder().AddPolicy(...).RequireClaim(...)` for ASP.NET routing-level auth; use `[Policy]` + `[RequirePolicy]` for request-level auth |
| ZA.Telemetry is a code-gen instrumentation library | Use vanilla OpenTelemetry; opt into `[Instrument]` per method |
| ZA.Mapping needs `<PrivateAssets>all</PrivateAssets>` | Set on the `<PackageReference>` in `MyApp.csproj` |
| `OrderId` / `CustomerId` use `[TypedId]` from ZA.ValueObjects | Auto-generated `.New()` factory + JSON converter |
| Switching a request to `IAuthorizedRequest<TPayload>` for Result-style auth | Under AOT publish, the deny path silently throws `AuthorizationDeniedException`. Add a `[ModuleInitializer]` carrier method with `[DynamicDependency(PublicMethods, typeof(Result<TPayload, AuthorizationFailure>))]` per `TPayload` you use. |

## 5. AOT-specific gotchas (as of EF Core 10)

EF Core's NativeAOT support is incomplete. This template works around the gaps:

| Issue | Workaround in template |
|---|---|
| `Database.MigrateAsync` / `EnsureCreatedAsync` use reflection-based design-time model building | Schema applied via embedded `schema.sql` script (regenerate with `dotnet ef migrations script` after entity changes) |
| Reflection-based handler scanning gets trimmed under AOT | `services.AddMediator().RegisterHandlersFromAssembly(typeof(Program).Assembly)` — combined with the slice convention, the trimmer keeps handlers reachable. For AOT-only builds you may need explicit per-handler registration. |
| Anonymous types lack source-gen JsonTypeInfo, fail at serialization under AOT | All response shapes are concrete records covered by `JsonContext` |

## 6. How to verify

```bash
# Build the whole solution
dotnet build MyApp.slnx

# Run all tests — unit + convention + integration
dotnet test MyApp.slnx

# Run a single slice's tests during inner-loop dev
dotnet test --filter "FullyQualifiedName~PlaceOrder"

# Run the BDN write-pipeline benchmark
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# Run the NBomber load test (two terminals)
dotnet run --project src/MyApp                              # terminal 1
dotnet run -c Release --project benchmarks/MyApp.LoadTest   # terminal 2
```

Pre-commit checklist:
- `dotnet build` is 0 errors, 0 warnings
- All convention tests pass (`dotnet test tests/MyApp.ConventionTests`)
- Any new slice has unit + integration tests
- Conventional commit message (`feat(orders): add CancelOrder slice`, etc.)
