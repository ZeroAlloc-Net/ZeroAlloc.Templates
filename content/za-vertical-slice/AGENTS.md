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
│   [Validate] → generated PlaceOrderCommandValidator      │
│   PlaceOrderHandler : IRequestHandler<…>                 │
│   PlaceOrderEndpoint.Map(IEndpointRouteBuilder)          │
│   public sealed class Order  ← persistence entity        │
└────────────────────┬─────────────────────────────────────┘
                     │ depends only on
                     ▼
┌──────────────────────────────────────────────────────────┐
│ src/MyApp/Common/      Ids (OrderId, CustomerId),        │
│ src/MyApp/Persistence/ Migrations/{Sqlite,Postgres}/*.sql│
│                        (embedded resources)              │
│ src/MyApp/Authorization/ [Policy] declarations           │
└──────────────────────────────────────────────────────────┘
```

- **`Features/<Area>/<UseCase>/<UseCase>.cs`** — one slice per use case. Holds the request + validator + handler + endpoint + (optionally) the persistence entity owned by that slice.
- **`Common/`** — primitives shared across slices: the `[ValueObject]` ids, `Money`, the `Error` catalog, telemetry setup.
- **Per-slice persistence** — each feature handler is a `public sealed partial class XxxHandler(IAsyncDbConnection conn)` with inline `[Query]`/`[Command]` partials and any row records co-located in the slice file. There is no central persistence type — the slice owns its data access shape, matching the vertical-slice principle that "everything a slice needs lives in the slice". `Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` holds the embedded SQL applied at startup by `ZeroAlloc.ORM.Migrations.MigrationRunner`.
- **`Authorization/Policies.cs`** — `[Policy]` declarations used by `[RequirePolicy(...)]` on requests.
- **`Program.cs`** — DI wiring + one explicit `XxxEndpoint.Map(app)` call per slice (no reflective endpoint discovery).

## 2. Convention rules (enforced)

`tests/MyApp.ConventionTests/VerticalSliceConventionRules.cs` enforces these via NetArchTest. Violations fail CI.

- Every type under `MyApp.Features` ending in `Command` or `Query` implements `IRequest<>`.
- Every type ending in `Handler` is `public sealed` and implements `IRequestHandler<,>`.
- Every endpoint is a `public static` class.

Cross-slice references are not a test rule: persistence entities such as `Order` are owned by one slice and read by others. Everything else stays slice-local by convention, checked at code review.

**Before adding a `using MyApp.Features.<OtherSlice>...;`, ask: "is this slice talking to another slice's internals?"** If yes, factor the shared type into `Common/` or send a request through `IMediator`.

## 3. How to add things

### Add a new use case (slice)

1. Create `src/MyApp/Features/<Area>/<UseCase>/<UseCase>.cs`.
2. Define the request as a `readonly record struct *Command`/`*Query` implementing `IRequest<Result<TResponse, Error>>`. Decorate with `[RequirePolicy("...")]` if authentication/authorization applies.
3. Put `[Validate]` on the request and attribute rules such as `[property: GreaterThan(0)]` on its members. ZA.Validation generates `*CommandValidator`, and the ZA.Mediator.Validation behaviour runs it before the handler.
4. Define `*Handler : IRequestHandler<*Command, Result<TResponse, Error>>`. Return `ValueTask<...>`, **not** `Task<...>`.
5. Define `*Endpoint` as a `public static class` with `public static void Map(IEndpointRouteBuilder)`. Wire `IMediator.Send` + map result to `Results.Created`/`Results.Ok`/`Results.Problem`.
6. If the slice needs to read or write rows, declare the relevant `[Query]`/`[Command]` partial methods directly on the handler class (mark the class `public sealed partial class XxxHandler(IAsyncDbConnection conn)`). Co-locate row records inside the class as `public sealed record XxxRow(...)`. Add SQL migration files at `src/MyApp/Persistence/Migrations/{Sqlite,Postgres}/NNN_<name>.sql` if the schema needs to change — the `MigrationRunner` picks them up by version on next startup.
7. **Mark the handler `[Scoped]`** (`using ZeroAlloc.Inject;`). Handler registration itself is automatic: the ZeroAlloc.Mediator generator emits `AddMediator()` with a concrete-type registration for every handler it finds at compile time, which is also how its dispatcher resolves them (never through `IRequestHandler<,>`). The attribute sets the lifetime — without it the handler is registered transient, and a handler holding the scoped `IAsyncDbConnection` should be scoped.
8. **Wire the endpoint.** Add one line near the bottom of `src/MyApp/Program.cs`:
   ```csharp
   YourEndpoint.Map(app);
   ```
   Explicit per-slice mapping keeps endpoint discovery reflection-free, which NativeAOT requires. Forgetting it means the route returns 404 at runtime — the slice's IntegrationTests will catch it.
9. Add a unit test in `tests/MyApp.UnitTests/Features/<Area>/<UseCase>/<UseCase>HandlerTests.cs`.
10. Add an integration test in `tests/MyApp.IntegrationTests/Features/<Area>/<UseCase>/<UseCase>EndpointTests.cs`.

### Add a value object

1. Create or extend `src/MyApp/Common/ValueObjects.cs` — a `readonly partial struct` with the `[ValueObject]` attribute from `ZeroAlloc.ValueObjects`, a `Value` property and a constructor, like `OrderId`.

### Add a validation rule

Add an attribute rule from `ZeroAlloc.Validation` to the request member, for example `[property: GreaterThan(0)]`. The `[Validate]` source generator regenerates the validator; there is no hand-written validator class to edit.

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
| `OrderId` / `CustomerId` are `[ValueObject]` structs over `int` from ZA.ValueObjects | Construct with `new OrderId(id)`; ZA.Serialisation generates the JSON converter |
| Switching a request to `IAuthorizedRequest<TPayload>` for Result-style auth | Under AOT publish, the deny path silently throws `AuthorizationDeniedException`. Add a `[ModuleInitializer]` carrier method with `[DynamicDependency(PublicMethods, typeof(Result<TPayload, AuthorizationFailure>))]` per `TPayload` you use. |

## 5. AOT publish

AOT publish is enabled (`<PublishAot>true</PublishAot>` in `MyApp.csproj`). Both
reflection sites that previously blocked it are gone:

1. **Endpoint discovery** — hand-listed via `XxxEndpoint.Map(app)` calls in
   `Program.cs` (see §3 step 8). One line per slice.
2. **Mediator handler registration** — source-generated. The ZeroAlloc.Mediator
   generator emits `AddMediator()` with every handler's concrete-type
   registration at compile time; `AddMyApp(...)` calls it. `[Scoped]` sets the
   lifetime (see §3 step 7).

The "Add a new use case" recipe in §3 above lists the one manual wiring step
each new slice needs. CI's `aot-publish-smoke-vs` and `real-run-smoke-vs` jobs
are the safety net — forgetting the `Map(app)` call in `Program.cs` surfaces at
the first HTTP request.

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
