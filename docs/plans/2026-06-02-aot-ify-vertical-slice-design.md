# Design — AOT-ify `za-vertical-slice` (closes B5)

**Status:** approved 2026-06-02
**Closes:** [B5 — AOT-ify za-vertical-slice (`<PublishAot>true</PublishAot>`)](../backlog.md#b5--aot-ify-za-vertical-slice-publishaottruepublishaot)
**Author:** brainstorming session
**Implementation plan:** to be created next via `superpowers:writing-plans`

## Context

The ZA.Templates EF Core → ZA.ORM swap (#152) eliminated two of B5's four originally-flagged AOT blockers (EF compiled model, EF LINQ-to-SQL read path). Two pre-existing reflection sites in `content/za-vertical-slice/src/MyApp/Program.cs` remain and still prevent `<PublishAot>true</PublishAot>`:

1. **Line 37** — `services.AddMediator().RegisterHandlersFromAssembly(typeof(Program).Assembly)`. ZA.Mediator's `RegisterHandlersFromAssembly` documents itself as not AOT-compatible; the comment in `ZeroAlloc.Mediator/MediatorBuilderExtensions.cs:28` directs AOT consumers to register handlers explicitly per `IRequestHandler<TReq, TResp>` closed-generic.
2. **Lines 239–247** — runtime assembly walk that finds every `*Endpoint` static class with a `Map(IEndpointRouteBuilder)` method and invokes it via `MethodInfo.Invoke(...)`. Pure reflection over `typeof(Program).Assembly.GetTypes()`.

A third `typeof(Program).Assembly` reference on line 210 (passed to `EmbeddedResourceMigrationSource`) is **AOT-safe** — the consumer reads `GetManifestResourceNames()` only, never `GetTypes()`.

`za-clean` has been AOT-publishing successfully since pre-swap by hand-listing handler registrations inside a dedicated `ApplicationServiceCollectionExtensions.cs` extension method exposed as `services.AddMyAppApplication(...)`. This design adopts the same pattern for vs.

The asymmetry with the eventual upstream source generator (`ZeroAlloc.Mediator.Generator` emitting `AddGeneratedHandlers()` automatically) is intentional and tracked as a separate brainstorm — see "Carry-forward" below.

## Architecture

Two concrete changes:

1. **Mediator + handler wiring moves out of Program.cs** into a single `Common/MyAppServiceCollectionExtensions.cs`. It hand-lists the `AddScoped<IRequestHandler<TReq, TResp>, THandler>()` calls and exposes one entry point — `services.AddMyApp(configureAuthorization)` — that wraps `AddMediator().WithValidation().WithAuthorization(...)` plus the per-slice handler registrations. Mirror of za-clean's `AddMyAppApplication(...)`.
2. **Endpoint walk → explicit Map calls.** The 16-line reflective walk is replaced with six `XxxEndpoint.Map(app)` lines in Program.cs, one per existing slice. Each slice's `*Endpoint` class keeps its `Map(IEndpointRouteBuilder)` signature; only the orchestration loses its reflection.

`<PublishAot>true</PublishAot>` is flipped on in `MyApp.csproj`; the "AOT publish intentionally NOT enabled" comment block is replaced with a 3-line note pointing at the registration file and the per-slice Map call site.

Result: vs publishes NativeAOT cleanly, matches za-clean's AOT posture, and the "add a slice" workflow gains two explicit lines per new slice — one in `MyAppServiceCollectionExtensions.cs`, one in `Program.cs`. CI's existing `aot-publish-smoke-vs` job becomes a genuine AOT gate instead of a build-only smoke.

## Components

### `src/MyApp/Common/MyAppServiceCollectionExtensions.cs` (new)

```csharp
using MyApp.Authorization;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Features.Orders.CancelOrder;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace MyApp;

public static class MyAppServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ZeroAlloc.Mediator pipeline, validation, authorization, and
    /// hand-listed slice handlers. The hand-list pattern is the AOT-friendly
    /// alternative to <c>RegisterHandlersFromAssembly</c> (which uses
    /// reflection-based assembly scanning incompatible with NativeAOT).
    /// Each new slice adds one line to the block below.
    /// </summary>
    public static IServiceCollection AddMyApp(
        this IServiceCollection services,
        Action<AuthorizationOptions> configureAuthorization)
    {
        services.AddMediator()
                .WithValidation()
                .WithAuthorization(configureAuthorization);

        // Per-slice handler registrations — keep in declaration order matching
        // the Features/<Area>/<UseCase>/ folder tree so adding a slice is
        // visually obvious.
        services.AddScoped<IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>, CreateCustomerHandler>();
        services.AddScoped<IRequestHandler<GetCustomerQuery, Result<CustomerDto, Error>>, GetCustomerHandler>();
        services.AddScoped<IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>, PlaceOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, Result<OrderDto, Error>>, GetOrderHandler>();
        services.AddScoped<IRequestHandler<ListOrdersQuery, Result<OrderPage, Error>>, ListOrdersHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCommand, UnitResult<Error>>, CancelOrderHandler>();

        return services;
    }
}
```

Key decisions:
- **File location**: `Common/` (matches `Telemetry.cs`, `ValueObjects.cs` — the existing "cross-cutting" home).
- **Method name**: `AddMyApp(...)` — symmetric with za-clean's `AddMyAppApplication(...)` (Clean Architecture layering means the Api project calls the Application layer's wrapper; vs is one assembly so the wrapper is named for the whole template). `dotnet new` source-replacement handles the rename at scaffold time.
- **Per-slice ordering**: matches `Features/Customers/...` → `Features/Orders/...` folder layout for visually-obvious diffs.
- **Method signature**: `Action<AuthorizationOptions>` callback — preserves the existing Program.cs pattern of `o => o.UseAccessor<HttpSecurityContextAccessor>()`.
- **`UnitResult<Error>` for CancelOrder**: command returns no payload; matches the existing handler signature.

### `src/MyApp/Program.cs` (modified)

Three blocks change.

**Block A — mediator wiring** (around line 32–39):

```diff
 builder.Services.AddHttpContextAccessor();
 builder.Services.AddHealthChecks();
 builder.Services.AddZeroAllocAuthorization();

-builder.Services.AddMediator()
-    .RegisterHandlersFromAssembly(typeof(Program).Assembly)
-    .WithValidation()
-    .WithAuthorization(o => o.UseAccessor<HttpSecurityContextAccessor>());
+builder.Services.AddMyApp(o => o.UseAccessor<HttpSecurityContextAccessor>());
```

**Block B — endpoint discovery** (around lines 222–247):

The reflective walk and its 16-line "vertical-slice convention…" comment block both go away. Replaced with:

```csharp
// Endpoint registrations — one Map call per slice, mirroring the
// Features/<Area>/<UseCase>/ tree. AOT publish requires the explicit calls;
// the pre-1.0 reflective walk was the last AOT-blocker (the other was
// reflection-based RegisterHandlersFromAssembly, now folded into AddMyApp).
PlaceOrderEndpoint.Map(app);
GetOrderEndpoint.Map(app);
ListOrdersEndpoint.Map(app);
CancelOrderEndpoint.Map(app);
CreateCustomerEndpoint.Map(app);
GetCustomerEndpoint.Map(app);

app.MapHealthChecks("/healthz");
app.Run();
```

Plus a batch of `using MyApp.Features.Customers.CreateCustomer;` (etc.) at the top of Program.cs to bring the `*Endpoint` types into scope.

**Block C — drop `using System.Reflection;`** (was only there for `BindingFlags`).

### `src/MyApp/MyApp.csproj` (modified)

- Add `<PublishAot>true</PublishAot>` in the existing `PropertyGroup` (next to `InvariantGlobalization`).
- Add `<TrimmerSingleWarn>true</TrimmerSingleWarn>` — mirrors za-clean's posture; collapses per-package trim warnings (OpenTelemetry, Sqlite, Npgsql) into one summary line per assembly.
- Replace the existing 9-line "AOT publish intentionally NOT enabled" comment block with a 3-line note pointing at `Common/MyAppServiceCollectionExtensions.cs` and the per-slice Map call site in Program.cs.

### `content/za-vertical-slice/AGENTS.md` (modified)

Existing §3 "Add a new use case" recipe gains two new steps after the existing "Create the slice files" step:

```markdown
6. **Register the handler.** Add one line to
   `src/MyApp/Common/MyAppServiceCollectionExtensions.cs`:
   ```csharp
   services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
   ```
   Keep the per-slice order matching the `Features/` tree so diffs are
   visually obvious. AOT publish refuses to find handlers via reflection,
   so forgetting this step surfaces at boot under
   `dotnet publish -p:PublishAot=true` — `real-run-smoke-vs` in CI is the
   safety net.

7. **Wire the endpoint.** Add one line to `src/MyApp/Program.cs`:
   ```csharp
   YourEndpoint.Map(app);
   ```
   Per-slice explicit registration is the AOT-friendly replacement for
   the pre-1.0 assembly walk. Forgetting it means the route returns 404
   at runtime — the IntegrationTests for that slice will catch it.
```

Existing §5 "AOT-specific gotchas (post-swap)" section gets one tiny update: drop the line saying AOT is intentionally disabled in vs.

## Testing strategy

CI gates do most of the verification:

- **`build-vs`** — confirms the new `MyAppServiceCollectionExtensions.cs` and rewritten Program.cs compile cleanly with `<PublishAot>true</PublishAot>` set.
- **`aot-publish-smoke-vs`** — currently runs `dotnet publish -p:PublishAot=true -r linux-x64` against a `dotnet new za-vertical-slice` scaffold. After this change becomes a genuine AOT gate; any future reflection site fails here.
- **`real-run-smoke-vs`** — boots the AOT-published binary and curls `/healthz`. Catches "handler not registered" or "endpoint not registered" runtime failures.

Local pre-push verification:

- `dotnet build content/za-vertical-slice/MyApp.slnx` — compile.
- `dotnet test content/za-vertical-slice/tests/{MyApp.UnitTests,MyApp.IntegrationTests,MyApp.ConventionTests}` — confirm none assert on reflective-discovery behavior (none should; convention tests check architectural boundaries, not Program.cs internals).
- `dotnet publish content/za-vertical-slice/src/MyApp -c Release -p:PublishAot=true -r win-x64` — if `vswhere.exe` is locally available; otherwise skip and let CI verify.

No new test files needed. The existing IntegrationTests for each slice (POST /orders → 201, GET /orders/{id} → 200, etc.) are the de-facto regression net for "handler is wired" and "endpoint is wired" — they fail loudly if either registration is missed.

## Carry-forward

**Upstream `ZeroAlloc.Mediator.Generator` extension to emit `AddGeneratedHandlers()`** — when shipped, both templates' hand-list blocks collapse to one method call. Brainstorm + design + plan tracked separately as B7 (TBD) once we have appetite for the upstream library work. Until then, the hand-list pattern works for both templates and ships AOT today.

## Out of scope

- Adding new slices, endpoints, or domain shapes — pure mechanics of AOT enablement.
- Changing the request/response shapes, validation rules, authorization scopes, or DTOs.
- Performance optimization beyond AOT publish (the swap already shipped the bench numbers).
- Refactoring how `WithValidation()` / `WithAuthorization()` work internally — they're already AOT-clean via their respective ZA generators.
- Modifying za-clean (already AOT-published; this design mirrors its pattern, doesn't change it).
