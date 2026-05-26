# `za-vertical-slice` Template Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `dotnet new za-vertical-slice -n MyApp` as the second template inside the existing `ZeroAlloc.Templates` NuGet pack — same 10-package showcase depth as `za-clean`, structurally inverted (single src project, one folder per use case, one file per slice). Bumps `ZeroAlloc.Templates` to **0.4.0** (additive minor).

**Architecture:** New folder `content/za-vertical-slice/` sibling to the existing `content/za-clean/`. Reuses all repo-level scaffolding (Directory.Build.props, release-please, template-pack csproj, CI workflow shape). The CI `build` / `aot-publish-smoke` / `real-run-smoke` jobs gain za-vertical-slice variants (parallel jobs, not matrix — keeps each variant readable). Repo-level config drift guard extends to verify `Directory.Build.props` / `Directory.Packages.props` / `global.json` match between root and `content/za-vertical-slice/` (same rule already enforced for `content/za-clean/`).

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 + Microsoft.Data.Sqlite, xUnit, NetArchTest, BenchmarkDotNet, NBomber, OpenTelemetry. ZeroAlloc.\* pre-wired (10 packages): Mediator, Mediator.Validation, Mediator.Authorization, Authorization, Validation, Results, Mapping, ValueObjects, Inject, Telemetry, Resilience, Rest (and the matching `*.Generator` analyzer packages). Versions inherited centrally via `Directory.Packages.props` — pinned identically to `za-clean`'s working set.

**Design doc:** `docs/plans/2026-05-26-za-vertical-slice-design.md` (committed at `d18103f`).

**Working branch:** `feat/za-vertical-slice` (already created off `main`; design committed).

---

## Scope estimate + timing flag

`za-clean` ships 107 .cs files across 10 projects (4 src + 3 tests + 3 benchmarks). `za-vertical-slice` collapses src to ONE project but keeps the test + benchmark fan-out, so realistic file count is **~70-90 .cs files** total. Plus CI workflow edits, template config, README, and the org-wide BACKLOG.md mark-shipped.

**This is a multi-session project.** Today's session has already shipped `ZeroAlloc.Flux 1.0.0` + `ZeroAlloc.Flux.Blazor 1.0.0` + `ZeroAlloc.EventSourcing.Mediator 1.0.0` (with one design pivot mid-flight). Realistic landing: **Phases 0–3 in this session** (scaffold + slices through PlaceOrder/GetOrder); the remaining phases (rest of slices, convention tests, benchmarks, CI smoke, ship) in a follow-up session. Each phase's commit count and rough effort is called out in the phase header.

---

## Phase 0 — Scaffold the template directory (2 hours, 6 tasks)

The cheapest approach: **clone `content/za-clean/` to `content/za-vertical-slice/`, then mutate**. Most root-level files (Directory.Build.props, Directory.Packages.props, global.json, MyApp.slnx, .template.config/template.json, AGENTS.md, CLAUDE.md, README.md) are nearly identical between templates — copy then edit.

### Task 0.1: Copy za-clean to za-vertical-slice

**Files:** entire `content/za-clean/` → `content/za-vertical-slice/`

**Steps:**
1. `cp -r content/za-clean content/za-vertical-slice` (or PowerShell equivalent: `Copy-Item content/za-clean content/za-vertical-slice -Recurse`).
2. Delete generated artifacts that shouldn't be committed: `bin/`, `obj/`, anything in `.template.config/` that's generated.
3. `git add content/za-vertical-slice && git status -s | head -20` — verify the new tree is staged but the build artifacts are gitignored.

**Commit:**
```bash
git add content/za-vertical-slice
git commit -m "chore(scaffold): clone content/za-clean to content/za-vertical-slice

Verbatim copy as the starting point for the new template. Subsequent
tasks mutate the layered structure (4 src csprojs) into the vertical-
slice structure (1 src csproj, one folder per use case)."
```

### Task 0.2: Adapt `.template.config/template.json`

**File:** `content/za-vertical-slice/.template.config/template.json`

Replace the identity + shortName + name + classifications block to match the new template:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Marcel Roozekrans",
  "classifications": ["Web", "API", "Vertical Slice", "ZeroAlloc"],
  "identity": "ZeroAlloc.Templates.VerticalSlice",
  "name": "ZeroAlloc Vertical Slice Web API",
  "shortName": "za-vertical-slice",
  "tags": { "language": "C#", "type": "solution" },
  "sourceName": "MyApp",
  "preferNameDirectory": true,
  "symbols": {
    "EnableSwagger": { "type": "parameter", "datatype": "bool", "defaultValue": "true" },
    "SeedDatabase": { "type": "parameter", "datatype": "bool", "defaultValue": "true" },
    "IncludeDocker": { "type": "parameter", "datatype": "bool", "defaultValue": "false" }
  }
}
```

**Commit:**
```bash
git add content/za-vertical-slice/.template.config/template.json
git commit -m "chore(template): set template.json identity for za-vertical-slice"
```

### Task 0.3: Adapt `MyApp.slnx` to the single-src-project layout

**File:** `content/za-vertical-slice/MyApp.slnx`

Replace the 4-src structure with a 1-src structure:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/MyApp/MyApp.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/MyApp.UnitTests/MyApp.UnitTests.csproj" />
    <Project Path="tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj" />
    <Project Path="tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj" />
  </Folder>
  <Folder Name="/benchmarks/">
    <Project Path="benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj" />
    <Project Path="benchmarks/MyApp.Benchmarks.Primitives/MyApp.Benchmarks.Primitives.csproj" />
    <Project Path="benchmarks/MyApp.LoadTest/MyApp.LoadTest.csproj" />
  </Folder>
</Solution>
```

(Note: the `ArchitectureTests` project from za-clean is renamed to `ConventionTests`.)

**Commit:**
```bash
git add content/za-vertical-slice/MyApp.slnx
git commit -m "chore(template): rewrite MyApp.slnx for single-src vertical-slice layout"
```

### Task 0.4: Delete the layered src projects, scaffold the single src project

**Files:**
- DELETE: `content/za-vertical-slice/src/MyApp.Application/` (entire tree)
- DELETE: `content/za-vertical-slice/src/MyApp.Domain/` (entire tree)
- DELETE: `content/za-vertical-slice/src/MyApp.Infrastructure/` (entire tree)
- RENAME: `content/za-vertical-slice/src/MyApp.Api/` → `content/za-vertical-slice/src/MyApp/`
- MODIFY: `content/za-vertical-slice/src/MyApp/MyApp.Api.csproj` → rename to `MyApp.csproj`, merge in package refs from the deleted projects, set `<RootNamespace>MyApp</RootNamespace>`

**Steps:**
1. `git rm -r content/za-vertical-slice/src/MyApp.Application content/za-vertical-slice/src/MyApp.Domain content/za-vertical-slice/src/MyApp.Infrastructure`
2. `git mv content/za-vertical-slice/src/MyApp.Api content/za-vertical-slice/src/MyApp`
3. `git mv content/za-vertical-slice/src/MyApp/MyApp.Api.csproj content/za-vertical-slice/src/MyApp/MyApp.csproj`
4. Edit `content/za-vertical-slice/src/MyApp/MyApp.csproj`:
   - `<RootNamespace>` → `MyApp`
   - Merge all `<PackageReference>` items from `MyApp.Application.csproj` (Mediator, Validation, Authorization, Mapping, Results, Inject, Telemetry, Resilience, plus all their `*.Generator` siblings) and `MyApp.Domain.csproj` (ValueObjects) and `MyApp.Infrastructure.csproj` (EF Core, JwtBearer, OpenTelemetry instrumentations, Rest packages). Deduplicate.
   - Keep `ZeroAllocAuthorizationOwnsPolicies = true` — the single src project now owns policies.
   - Drop all `<ProjectReference>` entries (no sibling src projects to reference).
5. Delete any files in `src/MyApp/` that are Application-layer-specific stragglers (the rename brought the Api files, but verify Program.cs is the only non-feature file kept).

After this task, `src/MyApp/` contains only the files that were under `src/MyApp.Api/` from za-clean. Most of them will be replaced in subsequent tasks.

**Verify (cannot build cleanly yet — references missing):**
```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
dotnet restore content/za-vertical-slice/MyApp.slnx
```
Expected: restore succeeds (no project references to resolve); build will fail because Program.cs references Application/Domain/Infrastructure types that no longer exist.

**Commit:**
```bash
git add content/za-vertical-slice/src/
git commit -m "chore(template): collapse 4-csproj src/ to single MyApp.csproj

Deletes MyApp.Application/Domain/Infrastructure. Renames MyApp.Api/ to
MyApp/ and the csproj accordingly. Merges all package references from
the deleted csprojs into the new MyApp.csproj. Build is intentionally
broken at this point — Program.cs still references deleted types.
Subsequent tasks rewire it."
```

### Task 0.5: Stub `Program.cs` with endpoint-discovery walk + DI wiring

**File:** `content/za-vertical-slice/src/MyApp/Program.cs`

Replace the za-clean Program.cs body with the vertical-slice equivalent. Key bits:

```csharp
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Mediator;
// ... other usings ...

var builder = WebApplication.CreateBuilder(args);

// DI wiring — same packages as za-clean.
builder.Services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly)
    .UseValidation()
    .UseAuthorization();

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=myapp.db;Cache=Shared"));

builder.Services.AddZeroAllocAuthorization();
builder.Services.AddZeroAllocValidation();
// Telemetry, JWT bearer, Swagger (conditional on EnableSwagger symbol), etc.

var app = builder.Build();

// Endpoint discovery — runtime assembly walk.
foreach (var endpointType in typeof(Program).Assembly
    .GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Endpoint"))
    .Where(t => t.GetMethod("Map", new[] { typeof(IEndpointRouteBuilder) }) is not null))
{
    endpointType.GetMethod("Map")!.Invoke(null, new object[] { app });
}

app.MapHealthChecks("/healthz");
app.Run();

public partial class Program { } // For WebApplicationFactory
```

Copy the JWT bearer + Swagger conditional blocks verbatim from za-clean's Program.cs. Adapt namespace references to point at `MyApp.Common`, `MyApp.Persistence` etc.

**Build verify:** still expected to fail (Persistence + Common types don't exist yet — Phase 1 + 2 create them). Move on.

**Commit:**
```bash
git add content/za-vertical-slice/src/MyApp/Program.cs
git commit -m "feat(template): Program.cs with DI wiring + endpoint-discovery walk

Assembly walk discovers every public static class *Endpoint with a
public static void Map(IEndpointRouteBuilder) method and calls each
at startup. Runtime walk (not source-generated); replaceable in v0.5
if startup time becomes a concern."
```

### Task 0.6: Adapt template-level doc files

**Files:**
- MODIFY: `content/za-vertical-slice/README.md` — replace za-clean's README with a vertical-slice-equivalent walkthrough. Same structure; different architecture description; same "10 packages pre-wired" callouts.
- MODIFY: `content/za-vertical-slice/AGENTS.md` — replace "Clean Architecture" references with "Vertical Slice Architecture". Keep ZA-package guidance unchanged.
- MODIFY: `content/za-vertical-slice/CLAUDE.md` — same edits as AGENTS.md.

**Commit:**
```bash
git add content/za-vertical-slice/README.md content/za-vertical-slice/AGENTS.md content/za-vertical-slice/CLAUDE.md
git commit -m "docs(template): replace Clean Architecture wording with Vertical Slice"
```

---

## Phase 1 — Common layer (1 hour, 4 tasks)

Shared types every slice references. Lives at `src/MyApp/Common/`, `Persistence/`, and `Authorization/`.

### Task 1.1: ValueObjects (TypedId)

**File:** `content/za-vertical-slice/src/MyApp/Common/ValueObjects.cs`

Mirror za-clean's TypedIds. At minimum: `OrderId` and `CustomerId`. Use ZeroAlloc.ValueObjects `[TypedId]` partial-struct attribute pattern.

```csharp
using ZeroAlloc.ValueObjects;

namespace MyApp.Common;

[TypedId]
public readonly partial record struct OrderId;

[TypedId]
public readonly partial record struct CustomerId;
```

The `OrderId.New()` static factory + JSON converter come from the generator.

**Verify:** `dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj` should now succeed for this file specifically (other parts still broken).

**Commit:** `feat(template): add TypedId value objects (OrderId, CustomerId)`.

### Task 1.2: Errors catalog

**File:** `content/za-vertical-slice/src/MyApp/Common/Errors.cs`

Shared `Error` types via `ZeroAlloc.Results`. At minimum: `NotFound`, `ValidationFailed`, `Unauthorized`, `Conflict`, plus a `ToProblem()` extension that converts an `Error` to ASP.NET Core `Results.Problem(...)`.

Port the same `ValidationError.cs` shape from za-clean's Application layer (it's the right value-type pattern); collapse to one file inside Common.

**Commit:** `feat(template): add Error catalog + ToProblem extension`.

### Task 1.3: Telemetry setup

**File:** `content/za-vertical-slice/src/MyApp/Common/Telemetry.cs`

Port za-clean's `MyApp.Api/Telemetry/Telemetry.cs` (or wherever it lives) — OTel resource setup + slice-level activity source. Wire it from Program.cs via `services.AddOpenTelemetry()` block (same code as za-clean).

**Commit:** `feat(template): add OpenTelemetry resource + ActivitySource setup`.

### Task 1.4: Authorization policies

**File:** `content/za-vertical-slice/src/MyApp/Authorization/Policies.cs`

Port the `[Policy]` declarations from za-clean's `MyApp.Application/Authorization/`. At minimum: `customer` (logged-in customer can place orders), `admin` (admin can list/cancel all orders).

Use `ZeroAlloc.Authorization` 2.1.0's `[Policy]` attribute pattern. Each policy is a static class with the policy logic.

**Verify:** `dotnet build content/za-vertical-slice/src/MyApp/MyApp.csproj` — Common + Authorization now exists; build should fail only on Persistence + slice references.

**Commit:** `feat(template): add Authorization policies (customer, admin)`.

---

## Phase 2 — Persistence (45 min, 2 tasks)

### Task 2.1: AppDbContext

**File:** `content/za-vertical-slice/src/MyApp/Persistence/AppDbContext.cs`

Port the `AppDbContext` from za-clean's `MyApp.Infrastructure/Persistence/AppDbContext.cs`. Adapt namespace to `MyApp.Persistence`. The DbSet shapes will be populated when slices add their entities — start with empty DbSets for `Order` and `Customer`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MyApp.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity configurations live in each slice's file. Slices that
        // own a persistence entity invoke modelBuilder.Entity<X>(b => ...) here
        // via partial OnModelCreating extensions if needed; v0.4 puts entity
        // configurations inline at the slice level.
        base.OnModelCreating(modelBuilder);
    }
}

// Stub entities — actual definitions live in the slices that create them.
public sealed class Order { }
public sealed class Customer { }
```

Note: this stubs `Order` and `Customer` in a way that compiles. The actual full entity definitions land in their owning slices (`PlaceOrder/PlaceOrder.cs` defines `Order` properly; `CreateCustomer/CreateCustomer.cs` defines `Customer`). The stubs are deleted in the slice tasks.

Build verify: should now compile for everything except the slice references.

**Commit:** `feat(template): add AppDbContext with Order + Customer DbSets`.

### Task 2.2: Initial EF Core migration

**Files:**
- Create: `content/za-vertical-slice/src/MyApp/Persistence/Migrations/<timestamp>_InitialCreate.cs`
- Create: `content/za-vertical-slice/src/MyApp/Persistence/Migrations/<timestamp>_InitialCreate.Designer.cs`
- Create: `content/za-vertical-slice/src/MyApp/Persistence/Migrations/AppDbContextModelSnapshot.cs`

Run `dotnet ef migrations add InitialCreate --project content/za-vertical-slice/src/MyApp` (from the repo root or wherever EF CLI can resolve). NOTE: this needs to wait until Phase 3's slice tasks have populated the entities, otherwise the migration will be empty. **Defer Task 2.2 to after Phase 3.**

(Move this task to Phase 3.7 in execution. Listed here for organizational completeness.)

---

## Phase 3 — Slices (4 hours, 6+ tasks, batchable)

The core of the template. Six slices, each as a single `.cs` file with request/validator/handler/endpoint/(entity). Each slice follows the canonical pattern from the design doc.

**Pattern for every slice:**

1. Create the slice file `content/za-vertical-slice/src/MyApp/Features/<Area>/<UseCase>/<UseCase>.cs`.
2. Define `*Command`/`*Query` record struct implementing `IRequest<Result<TResponse, Error>>` with `[RequirePolicy(...)]` where applicable.
3. Define `*Validator : AbstractValidator<*Command>`.
4. Define `*Handler : IRequestHandler<*Command, Result<TResponse, Error>>`.
5. Define `*Endpoint` static class with `public static void Map(IEndpointRouteBuilder)`.
6. If the slice owns a persistence entity (PlaceOrder owns `Order`; CreateCustomer owns `Customer`), define it as an `internal sealed class` in the same file. Delete the stub from `AppDbContext.cs` after the first slice that defines each entity.
7. Add a unit test in `tests/MyApp.UnitTests/Features/<Area>/<UseCase>/<UseCase>HandlerTests.cs`.
8. Add an integration test in `tests/MyApp.IntegrationTests/Features/<Area>/<UseCase>/<UseCase>EndpointTests.cs`.

### Task 3.1: PlaceOrder slice (canonical example — full TDD)

**Files:**
- Create: `content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs`
- Create: `content/za-vertical-slice/tests/MyApp.UnitTests/Features/Orders/PlaceOrder/PlaceOrderHandlerTests.cs`
- Create: `content/za-vertical-slice/tests/MyApp.IntegrationTests/Features/Orders/PlaceOrder/PlaceOrderEndpointTests.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Persistence/AppDbContext.cs` — delete the `public sealed class Order { }` stub now that PlaceOrder.cs defines the real entity.

**Step 1: Write the failing unit test**

`PlaceOrderHandlerTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using MyApp.Persistence;
using Xunit;

namespace MyApp.UnitTests.Features.Orders.PlaceOrder;

public sealed class PlaceOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_WithValidInput_PersistsAndReturnsOrderId()
    {
        await using var db = NewInMemoryDb();
        var handler = new PlaceOrderHandler(db);
        var cmd = new PlaceOrderCommand(CustomerId.New(), Total: 99.99m);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrder_WithZeroTotal_ValidationFails()
    {
        var validator = new PlaceOrderValidator();
        var result = validator.Validate(new PlaceOrderCommand(CustomerId.New(), Total: 0m));
        result.IsValid.Should().BeFalse();
    }

    private static AppDbContext NewInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(opts);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

**Step 2: Run, verify it fails (build error — PlaceOrderCommand doesn't exist)**

```bash
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/ -c Release --filter "FullyQualifiedName~PlaceOrderHandlerTests"
```

Expected: BUILD FAIL with `CS0246: PlaceOrderCommand could not be found`.

**Step 3: Implement the slice**

`PlaceOrder.cs`:
```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Features.Orders.PlaceOrder;

[RequirePolicy("customer")]
public readonly record struct PlaceOrderCommand(CustomerId CustomerId, decimal Total)
    : IRequest<Result<OrderId, Error>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.Total).GreaterThan(0).WithMessage("Total must be positive");
    }
}

public sealed class PlaceOrderHandler(AppDbContext db)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(OrderId.New(), cmd.CustomerId, cmd.Total);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id;
    }
}

public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(cmd, ct);
            return result.Match(id => Results.Created($"/orders/{id}", id), err => err.ToProblem());
        });
}

// Persistence entity owned by this slice.
internal sealed class Order
{
    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public decimal Total { get; private set; }

    // EF Core ctor.
    private Order() { }

    public Order(OrderId id, CustomerId customerId, decimal total) =>
        (Id, CustomerId, Total) = (id, customerId, total);
}
```

Delete the stub `public sealed class Order { }` from `AppDbContext.cs`.

**Step 4: Run tests — expect PASS**

```bash
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/ -c Release --filter "FullyQualifiedName~PlaceOrderHandlerTests"
```

Expected: 2/2 pass.

**Step 5: Write integration test**

`PlaceOrderEndpointTests.cs` (mirror the za-clean integration-test fixture pattern, hit `POST /orders`).

**Step 6: Run integration test — expect PASS** (depends on WebApplicationFactory setup from the test csproj; mirror za-clean's exact wiring).

**Step 7: Commit**

```bash
git add content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/ \
        content/za-vertical-slice/tests/MyApp.UnitTests/Features/Orders/PlaceOrder/ \
        content/za-vertical-slice/tests/MyApp.IntegrationTests/Features/Orders/PlaceOrder/ \
        content/za-vertical-slice/src/MyApp/Persistence/AppDbContext.cs
git commit -m "feat(template): PlaceOrder slice (canonical pattern)

Single file owning the request/validator/handler/endpoint/entity. Unit
test covers happy path + validation failure; integration test covers
the full HTTP pipeline. Order entity moved out of AppDbContext stub
into this slice — vertical-slice convention is for the slice that
creates an entity to own its definition."
```

### Tasks 3.2–3.6: remaining slices (skeleton form)

Each follows the same TDD shape as Task 3.1. Brief specs:

- **Task 3.2: GetOrder slice** — `GET /orders/{id}` → returns `OrderDto` (use `ZeroAlloc.Mapping` for the `Order → OrderDto` projection, demonstrating Mapping inside a slice). Test the not-found case → returns `Error.NotFound` → `404`.
- **Task 3.3: ListOrders slice** — `GET /orders` → returns `OrderDto[]`. Demonstrates pagination via query parameter. `[RequirePolicy("admin")]`.
- **Task 3.4: CancelOrder slice** — `DELETE /orders/{id}` → soft-cancels via state transition. `[RequirePolicy("admin")]`. Test happy path + already-cancelled (Conflict) + not-found.
- **Task 3.5: CreateCustomer slice** — `POST /customers` → returns `CustomerId`. Owns `Customer` entity (delete stub in AppDbContext.cs). Mirrors PlaceOrder shape.
- **Task 3.6: GetCustomer slice** — `GET /customers/{id}` → returns `CustomerDto`. Mirrors GetOrder shape.

Each task: write slice file, write unit test, write integration test, run, commit.

### Task 3.7: Initial EF Core migration (now that entities exist)

```bash
dotnet tool restore  # if dotnet-ef isn't local
cd content/za-vertical-slice/src/MyApp
dotnet ef migrations add InitialCreate
```

Commits 3 files (the migration + designer + snapshot). The migration will pick up `Order` and `Customer` from the slice-owned entities.

**Commit:** `feat(template): initial EF Core migration (Orders + Customers tables)`.

### Task 3.8: Build + full-suite green

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
dotnet restore content/za-vertical-slice/MyApp.slnx
dotnet build content/za-vertical-slice/MyApp.slnx -c Release
dotnet test content/za-vertical-slice/MyApp.slnx -c Release
```

Expected: every test passes (12 unit tests + 12 integration tests across 6 slices, roughly).

**No commit** — verification only. Push the branch at this point so CI can see progress:
```bash
git push -u origin feat/za-vertical-slice
```

Note: CI's existing jobs target `content/za-clean/` paths — they won't fail on the new content yet. Phase 4 + 5 add the new jobs.

---

## Phase 4 — Convention tests (1 hour, 3 tasks)

### Task 4.1: Create the ConventionTests project

**Files:**
- Create: `content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj` (copy from `MyApp.ArchitectureTests.csproj` shape; rename)
- Create: `content/za-vertical-slice/tests/MyApp.ConventionTests/VerticalSliceConventionRules.cs`

`MyApp.ConventionTests.csproj`: copy `MyApp.ArchitectureTests.csproj` from za-clean, rename, adjust ProjectReferences to point at `MyApp.csproj` (the single src project).

**Commit:** `chore(template): scaffold MyApp.ConventionTests project`.

### Task 4.2: NetArchTest rules adapted for vertical slice

`VerticalSliceConventionRules.cs`:

```csharp
using System.Reflection;
using MyApp.Persistence;
using NetArchTest.Rules;
using Xunit;
using ZeroAlloc.Mediator;

namespace MyApp.ConventionTests;

public class VerticalSliceConventionRules
{
    private static readonly Assembly App = typeof(Program).Assembly;

    [Fact]
    public void Every_command_or_query_implements_IRequest()
    {
        var result = Types.InAssembly(App)
            .That()
            .HaveNameEndingWith("Command").Or().HaveNameEndingWith("Query")
            .Should()
            .ImplementInterface(typeof(IRequest<>))
            .GetResult();
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Every_handler_class_is_sealed_and_public()
    {
        var result = Types.InAssembly(App)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .And()
            .BePublic()
            .GetResult();
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void No_slice_references_another_slices_internals()
    {
        // For each Features.<Area>.<UseCase> namespace, verify no type in it
        // references types in another Features.<Area2>.<UseCase2> namespace.
        // Implementation: enumerate all slice namespaces and apply
        // NetArchTest's HaveDependencyOnAny check.
        // ... (real implementation walks namespaces via reflection)
        var sliceNamespaces = App.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns?.StartsWith("MyApp.Features.") == true)
            .Distinct()
            .ToArray();

        foreach (var sliceNs in sliceNamespaces)
        {
            var otherSlices = sliceNamespaces.Where(n => n != sliceNs).ToArray();
            var result = Types.InAssembly(App)
                .That().ResideInNamespace(sliceNs)
                .Should()
                .NotHaveDependencyOnAny(otherSlices)
                .GetResult();
            Assert.True(result.IsSuccessful, $"Slice {sliceNs} references another slice's internals");
        }
    }
}
```

Run the convention tests; expect 3/3 pass given the slice structure already conforms.

**Commit:** `test(template): vertical-slice convention rules (NetArchTest)`.

### Task 4.3: Add to slnx + verify

Update `content/za-vertical-slice/MyApp.slnx` to include the new ConventionTests project (already added in Task 0.3; verify path is correct).

```bash
dotnet test content/za-vertical-slice/MyApp.slnx -c Release
```

Expected: every test passes including the new conventions.

**Commit (only if slnx needed adjustment):** `chore(template): register ConventionTests project in MyApp.slnx`.

---

## Phase 5 — Benchmarks (1 hour, 3 tasks)

Ports of za-clean's three benchmark projects. Same shape, adapted entity names.

### Task 5.1: MyApp.Benchmarks (BDN scenario via WebApplicationFactory)

Copy `content/za-clean/benchmarks/MyApp.Benchmarks/` to `content/za-vertical-slice/benchmarks/MyApp.Benchmarks/`. Adjust:
- ProjectReference to the new `src/MyApp/MyApp.csproj` (instead of MyApp.Api + MyApp.Application).
- The BDN scenario itself — replace `[Benchmark] public Task PlaceOrder_Clean()` with `[Benchmark] public Task PlaceOrder_VerticalSlice()`. Body unchanged; just the test name.

**Commit:** `feat(template): MyApp.Benchmarks (BDN PlaceOrder scenario)`.

### Task 5.2: MyApp.Benchmarks.Primitives

Copy verbatim from za-clean — ZA primitive micro-benchmarks (TypedId construction, Result allocation, etc.) — and adjust the project reference to point at the new MyApp.csproj.

**Commit:** `feat(template): MyApp.Benchmarks.Primitives (ZA primitive micro-benchmarks)`.

### Task 5.3: MyApp.LoadTest

Copy verbatim from za-clean — NBomber scenario hitting Kestrel on loopback. Adjust ProjectReference.

**Commit:** `feat(template): MyApp.LoadTest (NBomber loopback scenario)`.

After all three benchmark projects: `dotnet build content/za-vertical-slice/MyApp.slnx -c Release` should still succeed.

---

## Phase 6 — CI smoke + ship (1.5 hours, 5 tasks)

### Task 6.1: Config-drift guard for the new template

Edit `.github/workflows/ci.yml`. Find the "Verify root vs content/za-clean config files match" step. EXTEND it to also verify `content/za-vertical-slice/`:

```yaml
      - name: Verify root vs content config files match
        shell: bash
        run: |
          for file in Directory.Build.props Directory.Packages.props global.json; do
            for template in za-clean za-vertical-slice; do
              if ! diff -q "$file" "content/$template/$file" > /dev/null; then
                echo "::error::$file has drifted between repo root and content/$template/"
                diff "$file" "content/$template/$file" || true
                exit 1
              fi
            done
          done
```

**Commit:** `ci: extend config-drift guard to za-vertical-slice`.

### Task 6.2: Build + Test smoke job for za-vertical-slice

Duplicate the existing `build:` job in `ci.yml` to a new `build-vs:` job, adjusting paths from `content/za-clean/` to `content/za-vertical-slice/`. The job runs restore + build + test + primitives benchmark dry-run against the new template.

Also extend `tests/ZeroAlloc.Templates.SmokeTests/TemplateScaffoldsAndBuildsTests.cs` — refactor the existing `Template_installs_scaffolds_builds_and_tests_pass` test to a `[Theory]` parameterized over `["za-clean", "za-vertical-slice"]` with matching short-names. Or duplicate the test method (simpler diff, cleaner reading; subagent's call).

**Commit:** `ci: add build-vs job + extend SmokeTests to cover za-vertical-slice`.

### Task 6.3: AOT publish smoke + real-run smoke jobs

Two more job duplications: `aot-publish-smoke-vs:` and `real-run-smoke-vs:` mirroring the existing jobs but pointing at the new template.

The real-run-smoke job mints a JWT and hits `POST /orders` — make sure the route still exists at the same URL in za-vertical-slice (PlaceOrder slice's `MapPost("/orders")` matches za-clean's endpoint exactly, so the smoke test body is identical).

**Commit:** `ci: add aot-publish-smoke-vs + real-run-smoke-vs jobs`.

### Task 6.4: Repo-level README catalog update

Edit `README.md` (repo root). Add `za-vertical-slice` to the template catalog table/list, alongside the existing `za-clean` entry. Brief one-liner description + link to `content/za-vertical-slice/README.md`.

**Commit:** `docs(readme): add za-vertical-slice to template catalog`.

### Task 6.5: Push + PR + admin-merge + release

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
git push -u origin feat/za-vertical-slice
gh pr create --title "feat: za-vertical-slice template (Templates 0.4.0)" --body "..."
```

PR body includes:
- Summary (new template, parity with za-clean depth)
- Architecture (single src project, one folder per use case, one file per slice)
- What changed (file inventory)
- Backward compat (additive, no za-clean changes)
- Test plan (CI green: build + test + AOT smoke + real-run smoke + conventions)

Watch CI via `gh pr checks --watch`. If green, admin-merge:
```bash
gh pr merge --admin --squash --delete-branch
```

The merge commit should have a `Release-As: 0.4.0` trailer (add it to the squash-merge body via `--body` if release-please picks a different bump, or rely on conventional-commits `feat:` prefix → automatic minor bump from 0.3.x → 0.4.0).

After merge, release-please cuts the release PR. Admin-merge that PR too. NuGet propagation typically takes 2-5 minutes.

Verify NuGet:
```bash
curl -s "https://api.nuget.org/v3-flatcontainer/zeroalloc.templates/index.json" | tail -10
```
Expected: `"0.4.0"` appears.

Verify `dotnet new`:
```bash
dotnet new install ZeroAlloc.Templates::0.4.0
dotnet new list | grep za-vertical-slice
```
Expected: the new template listed.

### Task 6.6: Workspace BACKLOG.md hygiene

Edit `c:/Projects/Prive/ZeroAlloc/docs/BACKLOG.md` (workspace-local, not git-tracked). Find `### Template: Vertical Slice Architecture (`za-vertical-slice`)` and mark shipped — same `<details>`-collapsed format as the Fluxor and EventSourcing.Mediator entries updated earlier today.

Also update the "Template dependency readiness" table to mark `za-vertical-slice` as ✅ shipped.

**No commit** (workspace file).

---

## Verification checklist

- [ ] **Phase 0:** `content/za-vertical-slice/` exists with the single-src layout, `MyApp.csproj` consolidates all 10 ZA package references, Program.cs has the endpoint-discovery walk.
- [ ] **Phase 1:** `Common/` has TypedIds, Errors, Telemetry; `Authorization/Policies.cs` has policies.
- [ ] **Phase 2:** `AppDbContext` registered with Orders + Customers DbSets.
- [ ] **Phase 3:** 6 slice files, 12 unit tests, 12 integration tests, EF migration committed.
- [ ] **Phase 4:** `MyApp.ConventionTests` enforces slice conventions.
- [ ] **Phase 5:** 3 benchmark projects build green.
- [ ] **Phase 6:** CI green on all 4 jobs (build-vs, aot-publish-smoke-vs, real-run-smoke-vs, config-drift), PR + release-please both admin-merged, NuGet has 0.4.0, `dotnet new list` shows `za-vertical-slice`.

## Out of scope (deferred to v0.5+)

- Source-gen for endpoint registration (runtime assembly walk is fine for v0.4).
- Demonstrated cross-slice messaging example (notification handler in one slice listening to another slice's event). Documented but not coded.
- AOT-published variant of vertical slice — ASP.NET Core + EF + Mediator stack doesn't AOT cleanly yet (same constraint as za-clean's AOT smoke, which only verifies that the binary builds, not that it does anything meaningful at runtime). v0.4 includes the AOT smoke job to surface regressions, not to certify AOT correctness.
- Modular monolith template (`za-modular`) — separate template, future work.

---

## Notes on this plan's shape

- **One slice fully spelled out** (Task 3.1 PlaceOrder), the rest given as skeleton. Subagent fills in by pattern-matching against the canonical example + the design doc.
- **Heavy reliance on copy-from-za-clean** for benchmarks, test fixtures, doc files. Saves substantial rewriting; the vertical-slice template is structurally different in the slice arrangement, NOT in the benchmark/test/docs surface.
- **Phase 6 ship** assumes CI is green on every job; if a job fails (likely candidate: convention test rules being too strict for the actual file layout), the subagent diagnoses on-branch before merging.
- **Estimate:** Phases 0–2 ≈ 3-4 hours; Phase 3 ≈ 4 hours; Phases 4–6 ≈ 3-4 hours. **Total: 10-12 hours of focused work.** This is too much to land in today's remaining session given today's already-massive ship count.
