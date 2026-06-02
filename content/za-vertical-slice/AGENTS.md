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
│ src/MyApp/Persistence/ Migrations/{Sqlite,Postgres}/*.sql│
│                        (embedded resources)              │
│ src/MyApp/Authorization/ [Policy] declarations           │
└──────────────────────────────────────────────────────────┘
```

- **`Features/<Area>/<UseCase>/<UseCase>.cs`** — one slice per use case. Holds the request + validator + handler + endpoint + (optionally) the persistence entity owned by that slice.
- **`Common/`** — primitives shared across slices: TypedIds, the `Error` catalog, telemetry setup.
- **Per-slice persistence** — each feature handler is a `public sealed partial class XxxHandler(IAsyncDbConnection conn)` with inline `[Query]`/`[Command]` partials and any row records co-located in the slice file. There is no central persistence type — the slice owns its data access shape, matching the vertical-slice principle that "everything a slice needs lives in the slice". `Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` holds the embedded SQL applied at startup by `ZeroAlloc.ORM.Migrations.MigrationRunner`.
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
6. If the slice needs to read or write rows, declare the relevant `[Query]`/`[Command]` partial methods directly on the handler class (mark the class `public sealed partial class XxxHandler(IAsyncDbConnection conn)`). Co-locate row records inside the class as `public sealed record XxxRow(...)`. Add SQL migration files at `src/MyApp/Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` if the schema needs to change — the `MigrationRunner` picks them up by version on next startup.
7. **Mark the handler `[Scoped]`** (`using ZeroAlloc.Inject;`). ZA.Inject's generator picks this up and emits the concrete-type DI registration in `AddMyAppServices()` — without the attribute, `ZeroAlloc.Mediator`'s source-generated dispatch fails at runtime with `"no service for type ...Handler has been registered"` (it resolves handlers by their concrete type, not by `IRequestHandler<,>`).
8. **Register the handler's `IRequestHandler<,>` interface.** Add one line to `src/MyApp/Common/MyAppServiceCollectionExtensions.cs`:
   ```csharp
   services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
   ```
   Keep the per-slice order matching the `Features/` tree so diffs are visually obvious. AOT publish refuses to find handlers via reflection, so this hand-list is the AOT-friendly replacement for the pre-1.0 `RegisterHandlersFromAssembly(...)`.
9. **Wire the endpoint.** Add one line near the bottom of `src/MyApp/Program.cs`:
   ```csharp
   YourEndpoint.Map(app);
   ```
   Per-slice explicit registration is the AOT-friendly replacement for the pre-1.0 assembly walk. Forgetting it means the route returns 404 at runtime — the slice's IntegrationTests will catch it.
10. Add a unit test in `tests/MyApp.UnitTests/Features/<Area>/<UseCase>/<UseCase>HandlerTests.cs`.
11. Add an integration test in `tests/MyApp.IntegrationTests/Features/<Area>/<UseCase>/<UseCase>EndpointTests.cs`.

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

## 5. AOT publish

AOT publish is enabled (`<PublishAot>true</PublishAot>` in `MyApp.csproj`). Both
reflection sites that previously blocked it are gone:

1. **Endpoint discovery** — hand-listed via `XxxEndpoint.Map(app)` calls in
   `Program.cs` (see §3 step 9). One line per slice.
2. **Mediator handler registration** — hand-listed in
   `Common/MyAppServiceCollectionExtensions.cs` via `AddMyApp(...)` (the
   `IRequestHandler<,>` interfaces) plus the ZA.Inject-generated
   `AddMyAppServices()` (the concrete-type registrations from `[Scoped]`
   attributes). See §3 steps 7 + 8.

The "Add a new use case" recipe in §3 above lists the two manual steps that
each new slice needs. CI's `aot-publish-smoke-vs` and `real-run-smoke-vs` jobs
are the safety net — forgetting any of (a) `[Scoped]` on the handler,
(b) the interface registration in `MyAppServiceCollectionExtensions`, or
(c) the `Map(app)` call in `Program.cs` surfaces at the AOT publish or first
HTTP request.

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
