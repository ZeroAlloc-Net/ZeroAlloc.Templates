# ZA.Templates EF Core → ZA.ORM 1.1.0 Swap — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Replace EF Core 10 in both `za-clean` and `za-vertical-slice` templates with ZeroAlloc.ORM 1.1.0 + AdoNet.Async + raw providers, using `MigrationRunner` + embedded SQL for schema management. End state: both templates publish under NativeAOT without the EF compiled-model dance, and the canonical repository pattern uses ref-counted `IAsyncDbConnection` instead of the manual hold-the-slot pattern that caused PR #145's perf regression.

**Architecture:**
- `IAsyncDbConnection` registered scoped per-request (provider-selected: `SqliteAsyncDbConnection` or `NpgsqlAsyncDbConnection`).
- Repositories become `partial` classes with `[Query]` / `[Command]` methods; ZA.ORM generator emits the open / execute / close pipeline against the injected connection.
- Schema applied via `MigrationRunner` over embedded SQL resources at startup (replaces the existing `ApplyEmbeddedSchemaAsync` hand-rolled loader and the EF `Migrations.Sqlite/` + `Migrations.Postgres/` folders).
- za-clean is the canonical template — locks the recipe first. za-vertical-slice follows once the pattern is proven.

**Tech Stack:**
- ZeroAlloc.ORM 1.1.0 (Runtime, Abstractions, Generator)
- AdoNet.Async (SqliteAsyncDbConnection, NpgsqlAsyncDbConnection)
- Microsoft.Data.Sqlite 10.x, Npgsql 9.x (raw providers, no EF layer)
- .NET 10, NativeAOT publish target

---

## Phase A — za-clean Infrastructure swap

This phase locks the recipe. Every decision (DI shape, migration layout, value-object handling at the ADO boundary) gets made here once and re-applied to za-vertical-slice in Phase B.

### Task A1: Package swap in Directory.Packages.props

**Files:**
- Modify: `content/za-clean/Directory.Packages.props`
- Modify: `content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj`

**Step 1: Edit `Directory.Packages.props`**

Remove these four pins:
```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.2" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
<PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.12.0-beta.2" />
```

Add (under the ZeroAlloc.* block):
```xml
<PackageVersion Include="ZeroAlloc.ORM" Version="1.1.0" />
<PackageVersion Include="ZeroAlloc.ORM.Abstractions" Version="1.1.0" />
<PackageVersion Include="ZeroAlloc.ORM.Generator" Version="1.1.0" />
<PackageVersion Include="AdoNet.Async" Version="1.3.0" />
<PackageVersion Include="AdoNet.Async.Adapters" Version="1.3.0" />
<PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.8" />
<PackageVersion Include="Npgsql" Version="10.0.3" />
```

(Pin the exact Npgsql / AdoNet.Async / Microsoft.Data.Sqlite versions to whatever the ZA.ORM 1.1.0 nuspec declares — check `dotnet add package --dry-run` first.)

**Step 2: Edit `MyApp.Infrastructure.csproj` `PackageReference` list**

Remove `Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`.

Add `ZeroAlloc.ORM`, `ZeroAlloc.ORM.Abstractions`, `ZeroAlloc.ORM.Generator` (with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`), `AdoNet.Async`, `AdoNet.Async.Adapters`, `Microsoft.Data.Sqlite`, `Npgsql`. (`AdoNet.Async.Adapters` is provider-agnostic — `.AsAsync()` wraps any `DbConnection`. There are no provider-specific AdoNet.Async sub-packages.)

**Step 3: Confirm `dotnet restore` succeeds**

```bash
cd content/za-clean && dotnet restore MyApp.slnx
```

Expected: restore green. Build will fail (EF types not resolved) — that's Task A3's cleanup work.

**Step 4: Commit**

```bash
git add content/za-clean/Directory.Packages.props content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj
git commit -m "build(za-clean): swap EF Core packages for ZA.ORM 1.1.0 + AdoNet.Async"
```

---

### Task A2: Author embedded SQL migrations

**Files:**
- Create: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Sqlite/001_initial_schema.sql`
- Create: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations/Postgres/001_initial_schema.sql`
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/schema.sql`
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/schema.postgres.sql`

**Step 1: Author `Migrations/Sqlite/001_initial_schema.sql`**

Folder-scoped per provider so the two are discoverable as separate prefixes (`EmbeddedResourceMigrationSource` doesn't ship a provider-suffix filter — it scopes by `resourceNamespacePrefix`). Hoist the body of the existing `schema.sql` minus the `__EFMigrationsHistory` table and the EF history INSERT (MigrationRunner uses its own `__zaorm_migrations` table). Keep transactional boundaries — MigrationRunner wraps each migration in a transaction itself, so the file should NOT contain `BEGIN`/`COMMIT`.

```sql
CREATE TABLE "Orders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    "CustomerId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Total" TEXT NOT NULL
);

CREATE TABLE "OrderLines" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLines" PRIMARY KEY AUTOINCREMENT,
    "Sku" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "Price" TEXT NOT NULL,
    "OrderId" INTEGER NOT NULL,
    CONSTRAINT "FK_OrderLines_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OrderLines_OrderId" ON "OrderLines" ("OrderId");
```

**Step 2: Author `Migrations/Postgres/001_initial_schema.sql`**

Same shape but with Postgres types (the `IF NOT EXISTS` dance from the EF version is no longer needed — MigrationRunner won't re-run a migration recorded in `__zaorm_migrations`).

```sql
CREATE TABLE "Orders" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "CustomerId" integer NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Total" text NOT NULL,
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderLines" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "Sku" character varying(64) NOT NULL,
    "Quantity" integer NOT NULL,
    "Price" text NOT NULL,
    "OrderId" integer NOT NULL,
    CONSTRAINT "PK_OrderLines" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OrderLines_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OrderLines_OrderId" ON "OrderLines" ("OrderId");
```

**Step 3: Wire as embedded resources**

In `MyApp.Infrastructure.csproj` (project file, not Directory.Packages.props):

```xml
<ItemGroup>
  <EmbeddedResource Include="Persistence/Migrations/**/*.sql" />
</ItemGroup>
```

Resource names emitted by MSBuild become `MyApp.Infrastructure.Persistence.Migrations.Sqlite.001_initial_schema.sql` and `MyApp.Infrastructure.Persistence.Migrations.Postgres.001_initial_schema.sql`, which `EmbeddedResourceMigrationSource` can prefix-scope per provider in Task A6.

**Step 4: Delete old schema files**

```bash
git rm content/za-clean/src/MyApp.Infrastructure/Persistence/schema.sql
git rm content/za-clean/src/MyApp.Infrastructure/Persistence/schema.postgres.sql
```

**Step 5: Commit**

```bash
git add content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations content/za-clean/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj
git commit -m "feat(za-clean): add ZA.ORM-shaped embedded SQL migrations (sqlite + postgres)"
```

---

### Task A3: Delete EF artifacts

**Files:**
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/AppDbContext.cs`
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/CompiledModel/` (entire folder)
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/Configurations/` (entire folder)
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Sqlite/` (entire folder)
- Delete: `content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Postgres/` (entire folder)
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/MoneyConverter.cs` — keep, but verify it's a static `ToStorage`/`FromStorage` pair (no EF `ValueConverter<,>` dependency). If it inherits from EF, rewrite as a plain static helper.

**Step 1: Audit MoneyConverter.cs**

```bash
cat content/za-clean/src/MyApp.Infrastructure/Persistence/MoneyConverter.cs
```

If it derives from `Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<,>`, rewrite as:

```csharp
namespace MyApp.Infrastructure.Persistence;

internal static class MoneyConverter
{
    public static string ToStorage(Money money) => money.ToString();
    public static Money FromStorage(string raw) => Money.Parse(raw);
}
```

If it's already a plain static class (as `OrderRepository.cs` line 93 suggests — `MoneyConverter.FromStorage(reader.GetString(2))`), leave as-is.

**Step 2: Delete the EF folders and files**

```bash
git rm content/za-clean/src/MyApp.Infrastructure/Persistence/AppDbContext.cs
git rm content/za-clean/src/MyApp.Infrastructure/Persistence/DesignTimeDbContextFactory.cs
git rm -r content/za-clean/src/MyApp.Infrastructure/Persistence/CompiledModel/
git rm -r content/za-clean/src/MyApp.Infrastructure/Persistence/Configurations/
git rm -r content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Sqlite/
git rm -r content/za-clean/src/MyApp.Infrastructure/Persistence/Migrations.Postgres/
```

**Step 3: Confirm `Persistence/` is now `MoneyConverter.cs` + `OrderRepository.cs` + `Migrations/` only**

```bash
ls content/za-clean/src/MyApp.Infrastructure/Persistence/
```

Expected: `Migrations  MoneyConverter.cs  OrderRepository.cs`.

`OrderRepository.cs` still references `AppDbContext` — Task A4 rewrites it. Build will still fail. That's expected.

**Step 4: Commit**

```bash
git add -u content/za-clean/src/MyApp.Infrastructure/Persistence/
git commit -m "refactor(za-clean): drop EF artifacts (AppDbContext + compiled model + EF migrations)"
```

---

### Task A4: Rewrite OrderRepository as a ZA.ORM partial

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs`

This is the centerpiece swap. The existing repository hand-rolls the manual hold-the-slot pattern (lines 35–51 of the old file). The new shape lets the ZA.ORM generator emit ref-counted open/close.

**Step 1: Replace OrderRepository.cs entirely**

```csharp
using System.Data.Async;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.ORM;

namespace MyApp.Infrastructure.Persistence;

[Scoped]
public sealed partial class OrderRepository(IAsyncDbConnection conn) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        var orderId = await InsertOrderAsync(
            order.CustomerId.Value,
            order.Status.ToString(),
            MoneyConverter.ToStorage(order.Total),
            ct).ConfigureAwait(false);

        foreach (var line in order.Lines)
        {
            await InsertOrderLineAsync(
                orderId,
                line.Sku,
                line.Quantity,
                MoneyConverter.ToStorage(line.Price),
                ct).ConfigureAwait(false);
        }
    }

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var tuple = await ReadOrderAsync(id.Value, ct).ConfigureAwait(false);
        if (tuple is null) return null;

        var (head, lines) = tuple.Value;
        var orderLines = new List<OrderLine>(lines.Count);
        foreach (var ln in lines)
        {
            orderLines.Add(new OrderLine(ln.Sku, ln.Quantity, MoneyConverter.FromStorage(ln.Price)));
        }

        return Order.Materialize(
            id,
            new CustomerId(head.CustomerId),
            Enum.Parse<OrderStatus>(head.Status),
            MoneyConverter.FromStorage(head.Total),
            orderLines);
    }

    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Status\", \"Total\") VALUES (@customerId, @status, @total)",
        Kind = CommandKind.Identity)]
    private partial Task<int> InsertOrderAsync(int customerId, string status, string total, CancellationToken ct);

    [Command(
        "INSERT INTO \"OrderLines\" (\"OrderId\", \"Sku\", \"Quantity\", \"Price\") VALUES (@orderId, @sku, @quantity, @price)")]
    private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, CancellationToken ct);

    [Query(
        "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
        "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
    private partial Task<(OrderHeadRow Head, IReadOnlyList<OrderLineRow> Lines)?> ReadOrderAsync(int id, CancellationToken ct);

    private sealed record OrderHeadRow(int CustomerId, string Status, string Total);
    private sealed record OrderLineRow(string Sku, int Quantity, string Price);
}
```

Key contrast vs. the old file:
- No manual `conn.State != ConnectionState.Open` dance — generator emits ref-counted open/close per the v1.0 design.
- Multi-result-set query expressed as a tuple return; ZA.ORM emits the `NextResultAsync` walk.
- `Order.AddAsync` becomes an explicit identity-returning `INSERT` + per-line `INSERT` — no `SaveChanges` change tracker. This is the AOT-clean shape; EF's `AddAsync`/`SaveChanges` was only working because the compiled model bypassed the reflection-based design-time pipeline.

**Step 2: Build**

```bash
cd content/za-clean && dotnet build src/MyApp.Infrastructure/MyApp.Infrastructure.csproj
```

Expected: compile fails — `Program.cs`, `InfrastructureServiceCollectionExtensions.cs`, and `SeedData.cs` still reference `AppDbContext`. Tasks A5–A7 fix those. Generator emit for `OrderRepository` should succeed (visible under `obj/Debug/net10.0/generated/ZeroAlloc.ORM.Generator/`).

**Step 3: Commit**

```bash
git add content/za-clean/src/MyApp.Infrastructure/Persistence/OrderRepository.cs
git commit -m "feat(za-clean): rewrite OrderRepository as ZA.ORM partial (ref-counted lifecycle)"
```

---

### Task A5: Rewire InfrastructureServiceCollectionExtensions

**Files:**
- Modify: `content/za-clean/src/MyApp.Infrastructure/InfrastructureServiceCollectionExtensions.cs`

**Step 1: Replace the `AddDbContextPool` block**

`MigrationRunner` is NOT registered in DI — its ctor takes an `IAsyncDbConnection` directly, and the connection lifetime mismatch (singleton runner vs. scoped per-request connection) makes DI registration awkward. The runner is built ad-hoc in `Program.cs` (Task A6) with its own short-lived connection. Here we only register `IAsyncDbConnection` for the application path:

```csharp
using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Infrastructure.External;
using Npgsql;
using ZeroAlloc.Rest.Resilience;
using ZeroAlloc.Rest.SystemTextJson;

namespace MyApp.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMyAppInfrastructure(
        this IServiceCollection services,
        string provider,
        string connectionString,
        string shippingBaseUrl)
    {
        var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);

        // IAsyncDbConnection registered scoped — ZA.ORM-generated repositories
        // pull a fresh connection per HTTP request and the generator-emitted
        // ref-counted open/close pipeline runs against it.
        services.AddScoped<IAsyncDbConnection>(_ =>
        {
            if (isPostgres)
            {
                return new NpgsqlConnection(connectionString).AsAsync();
            }
            return new SqliteConnection(connectionString).AsAsync();
        });

        services.AddMyAppInfrastructureServices();

        services.AddRestResilience<
            IShippingQuoteHttpClient,
            ShippingQuoteHttpClientClient,
            IShippingQuoteHttpClientResilienceProxy>(
            (inner, sp) => new IShippingQuoteHttpClientResilienceProxy(
                inner,
                sp.GetRequiredService<ZeroAlloc.Resilience.RetryPolicy>(),
                sp.GetRequiredService<ZeroAlloc.Resilience.TimeoutPolicy>()),
            opts =>
            {
                opts.BaseAddress = new Uri(shippingBaseUrl);
                opts.UseSerializer<SystemTextJsonSerializer>();
            });

        services.AddSingleton(new ZeroAlloc.Resilience.RetryPolicy(maxAttempts: 3, backoffMs: 200, jitter: true, perAttemptTimeoutMs: 0));
        services.AddSingleton(new ZeroAlloc.Resilience.TimeoutPolicy(5_000));

        return services;
    }
}
```

**Step 2: Commit**

```bash
git add content/za-clean/src/MyApp.Infrastructure/InfrastructureServiceCollectionExtensions.cs
git commit -m "feat(za-clean): register IAsyncDbConnection per provider in Infrastructure DI"
```

---

### Task A6: Rewire Program.cs

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/Program.cs`

**Step 1: Replace the `ApplyEmbeddedSchemaAsync` block**

Lines 157–~270 of the current Program.cs hand-roll the embedded schema loader against `AppDbContext.Database.GetDbConnection()`. Replace the whole block with a `MigrationRunner` invocation. The runner builds its own short-lived connection — it does not share the per-request `IAsyncDbConnection` registered in DI (lifetime mismatch + the runner needs to open/close around the whole apply loop, not per-statement):

```csharp
// Apply schema on startup. `Database:SchemaStrategy` controls how:
//   EmbeddedScript  (default) — run ZA.ORM MigrationRunner over the
//                                Persistence/Migrations/{Sqlite,Postgres}/*.sql resources.
//   Skip            — assume an external pipeline applied the schema (CI,
//                     production migration tooling, container init-script).
var schemaStrategy = app.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    var schemaProvider = app.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
    var isPostgresSchema = string.Equals(schemaProvider, "Postgres", StringComparison.OrdinalIgnoreCase);

    System.Data.Common.DbConnection raw = isPostgresSchema
        ? new Npgsql.NpgsqlConnection(dbConnString)
        : new Microsoft.Data.Sqlite.SqliteConnection(dbConnString);

    await using (raw.ConfigureAwait(false))
    {
        var asyncConn = raw.AsAsync();
        await asyncConn.OpenAsync().ConfigureAwait(false);

        var source = new ZeroAlloc.ORM.Migrations.EmbeddedResourceMigrationSource(
            assembly: typeof(MyApp.Infrastructure.Persistence.OrderRepository).Assembly,
            resourceNamespacePrefix: isPostgresSchema
                ? "MyApp.Infrastructure.Persistence.Migrations.Postgres."
                : "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");

        ZeroAlloc.ORM.Migrations.IMigrationDialect dialect = isPostgresSchema
            ? new ZeroAlloc.ORM.Migrations.PostgresMigrationDialect()
            : new ZeroAlloc.ORM.Migrations.SqliteMigrationDialect();

        var runner = new ZeroAlloc.ORM.Migrations.MigrationRunner(asyncConn, source, dialect);
        var applied = await runner.RunAsync().ConfigureAwait(false);
        app.Logger.LogInformation("Applied {Count} ZA.ORM migrations on startup", applied.Count);

        await asyncConn.CloseAsync().ConfigureAwait(false);
    }
}
```

(`dbConnString` is the same connection string Program.cs already reads from configuration for the DI registration — reuse the existing local.)

Delete the `ApplyEmbeddedSchemaAsync` static method below it (no longer needed).

**Step 2: Remove the `AppDbContext` `using`**

Drop the `using MyApp.Infrastructure.Persistence;` line if it's now unused (Program.cs likely had it only for the `AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>()` call).

**Step 3: Build the whole solution**

```bash
cd content/za-clean && dotnet build MyApp.slnx
```

Expected: green build except possibly SeedData.cs (Task A7).

**Step 4: Commit**

```bash
git add content/za-clean/src/MyApp.Api/Program.cs
git commit -m "refactor(za-clean): drop ApplyEmbeddedSchemaAsync, use ZA.ORM MigrationRunner"
```

---

### Task A7: Rewire SeedData

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/SeedData.cs`

**Step 1: Inspect current SeedData**

```bash
cat content/za-clean/src/MyApp.Api/SeedData.cs
```

If it uses `AppDbContext db => db.Orders.Add(...) + db.SaveChangesAsync()`, replace with a call to `IOrderRepository.AddAsync(order, ct)` resolved from the scope (or rewrite as a thin static helper that takes `IOrderRepository` directly).

**Step 2: Verify**

```bash
cd content/za-clean && dotnet build MyApp.slnx
```

Expected: green build.

**Step 3: Commit**

```bash
git add content/za-clean/src/MyApp.Api/SeedData.cs
git commit -m "refactor(za-clean): seed via IOrderRepository instead of AppDbContext"
```

---

### Task A8: Integration tests pass

**Files:**
- Modify (if needed): `content/za-clean/tests/MyApp.IntegrationTests/**`

**Step 1: Run integration tests**

```bash
cd content/za-clean && dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: tests likely fail because the test factory shared an EF-style kept-alive in-memory Sqlite connection across the session (referenced in old OrderRepository.cs comment, lines 33–34). The new shape registers `IAsyncDbConnection` as scoped — each test gets a fresh connection, which won't share the in-memory database.

**Step 2: Update test fixture connection strategy**

Two options, pick the simpler one that matches the IntegrationTests scaffold:
  a) Switch the in-memory connection string to a file-backed `Data Source=test_<guid>.db` per test class, cleanup in `IAsyncLifetime.DisposeAsync`.
  b) Register a singleton `SqliteConnection` (kept open for the test session) and have `IAsyncDbConnection` wrap it; bypass the per-scope factory in the test composition root only.

Option (b) preserves the existing in-memory behavior with minimal churn. Apply that.

**Step 3: Re-run tests**

```bash
dotnet test tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj
```

Expected: green.

**Step 4: ArchitectureTests sanity**

```bash
dotnet test tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj
```

If any architecture rules pinned EF Core layer dependencies (e.g. "Infrastructure must reference EntityFrameworkCore"), update them to reference ZA.ORM instead.

**Step 5: UnitTests**

```bash
dotnet test tests/MyApp.UnitTests/MyApp.UnitTests.csproj
```

Expected: unaffected unless any unit test mocked `AppDbContext`. If so, mock `IOrderRepository` directly (Application-layer tests shouldn't know about the persistence shape anyway).

**Step 6: Commit**

```bash
git add -u content/za-clean/tests/
git commit -m "test(za-clean): adapt fixtures to IAsyncDbConnection scope shape"
```

---

### Task A9: Benchmarks + AOT smoke

**Files:**
- Modify: `content/za-clean/benchmarks/MyApp.Benchmarks/WritePipelineBench.cs`
- Modify: any benchmark that referenced `AppDbContext`.

**Step 1: Run benchmarks (Debug build, smoke only)**

```bash
cd content/za-clean && dotnet build -c Release benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj
```

Expected: green build. (Don't capture numbers in this task — Phase E does that.)

**Step 2: AOT publish smoke**

```bash
cd content/za-clean && dotnet publish src/MyApp.Api/MyApp.Api.csproj -c Release -r win-x64 /p:PublishAot=true
```

Expected: green publish. The compiled-model dance is gone, so AOT shouldn't even need to chew on the EF reflection pipeline anymore. This is the headline win.

If AOT trim warnings fire on `ZeroAlloc.ORM.Migrations.EmbeddedResourceMigrationSource` (it reads from `Assembly.GetManifestResourceNames()`), confirm the resource enumeration is annotated `RequiresDynamicCode`/`RequiresUnreferencedCode` per the v1.1 design — it should be safe for AOT but may emit IL2026 warnings unless suppressed.

**Step 3: Commit**

```bash
git add -u content/za-clean/benchmarks/
git commit -m "test(za-clean): benchmark + AOT publish green after ORM swap"
```

---

## Phase B — za-vertical-slice swap

Recipe locked in Phase A. This phase replays it on the 2-csproj template. Tasks here mirror Phase A but compressed because the structural decisions are already made.

### Task B1: Package swap

Mirror Task A1 against `content/za-vertical-slice/Directory.Packages.props` and `content/za-vertical-slice/src/MyApp/MyApp.csproj`.

Commit: `build(za-vertical-slice): swap EF Core for ZA.ORM 1.1.0 + AdoNet.Async`

### Task B2: Embedded migrations + delete EF artifacts

Inventory first:

```bash
ls content/za-vertical-slice/src/MyApp/Persistence/
```

Author `Migrations/001_initial_schema.sqlite.sql` (+ `.postgres.sql` if vs ships a Postgres variant) and delete the EF `schema.sql`, `AppDbContext`, `Migrations.Sqlite/`, etc., mirroring Tasks A2 + A3.

Commit: `feat(za-vertical-slice): replace EF schema/migrations with embedded SQL`

### Task B3: Rewire DI + repositories

The vertical-slice template's persistence sits inside features rather than under an Infrastructure layer. For each `EndpointName/EndpointHandler.cs` (or feature file) that injects `AppDbContext`, switch to `IAsyncDbConnection` and emit query methods inline as `partial` (vertical-slice convention — keep persistence collocated with the feature).

DI registration goes wherever the vertical-slice scaffolds its composition (likely `Program.cs` or a `ServiceCollectionExtensions`). Mirror Task A5.

Commit: `feat(za-vertical-slice): wire IAsyncDbConnection + per-feature query partials`

### Task B4: Tests + AOT smoke

Mirror Tasks A8 + A9 — adjust test fixtures if any test holds a connection alive, and confirm AOT publish goes green.

Commit: `test(za-vertical-slice): green after ORM swap`

---

## Phase C — `dotnet new` smoke test both templates

This phase consumes the templates the way a real adopter would.

### Task C1: Pack templates locally

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates
dotnet pack templates/ZeroAlloc.Templates.csproj -c Release -o ./local-feed
```

### Task C2: Install + scaffold za-clean

```bash
dotnet new uninstall ZeroAlloc.Templates 2>&1 | grep -q "is not installed" || dotnet new uninstall ZeroAlloc.Templates
dotnet new install ./local-feed/ZeroAlloc.Templates.<version>.nupkg

cd /tmp && rm -rf SmokeClean
dotnet new za-clean -n SmokeClean -o SmokeClean
cd SmokeClean
dotnet build
dotnet test
dotnet publish src/SmokeClean.Api/SmokeClean.Api.csproj -c Release -r win-x64 /p:PublishAot=true
```

Expected: each step green.

### Task C3: Install + scaffold za-vertical-slice

Same as C2 against `za-vertical-slice`.

### Task C4: Smoke output capture

Save the dotnet new + build + test + publish log under `docs/smoke/2026-06-01-orm-swap-smoke.md`. Useful as the PR's verification record.

Commit: `test(templates): smoke-tested both templates via dotnet new`

---

## Phase D — Release

### Task D1: Update template READMEs

`content/za-clean/README.md` and `content/za-vertical-slice/README.md` both reference EF Core in their "Persistence" sections. Replace with ZA.ORM language; link to the ZA.ORM cookbook (`https://orm.zeroalloc.net/cookbook/`) for deeper reading.

### Task D2: Update root README

Templates root `README.md` likely lists EF Core in the "What's inside" matrix. Swap for ZA.ORM 1.1.0.

### Task D3: Conventional-commit summary + release-please

The merged PR commits in Phases A–C should produce a release-please-suggested minor bump (`feat(za-clean): ...`, `feat(za-vertical-slice): ...`). Confirm release-please's title proposal matches `chore(main): release ZeroAlloc.Templates 0.10.0` (or 0.9.2 if release-please reads only `feat`/`fix` ratios and prefers patch).

If we want explicit minor on the swap, include one `feat!` or use the `release-as` override.

### Task D4: Carry-forward note

Add an entry to `docs/backlog.md` capturing the deferred items (e.g. value-object first-class binding in ZA.ORM, the `IOrderRepository` write-path benchmark refresh after Postgres becomes available).

Commit: `docs(release): prepare 0.10.0 ORM swap release`

---

## Summary

Eight tasks in Phase A, four in Phase B, four in Phase C, four in Phase D — 20 tasks total. Each task is a discrete commit. The Phase A → Phase B handoff is the locked-recipe moment; Phase B should move fast.

**Critical reminders:**
- `OrderRepository.cs` (old) is the textbook **anti-pattern** the swap cures. Reviewers should look at the new file and confirm zero `conn.State` checks, zero `OpenAsync` outside generator-emitted code.
- `MigrationRunner` is sufficient — do NOT roll a hand-rolled schema loader. The whole point of v1.1 was to ship this so templates don't need to.
- AOT publish is the canary. If `dotnet publish -r win-x64 /p:PublishAot=true` goes green on both templates with zero warnings beyond known ZA.ORM-suppressed IL202x, the swap is done.
