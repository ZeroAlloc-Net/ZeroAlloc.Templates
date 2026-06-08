# Output Caching for Read-by-Id Endpoints — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add ASP.NET Core OutputCaching to both templates' GET-by-id endpoints, with tag-based eviction on writes, configurable TTL via appsettings, and integration tests proving cache hit / eviction / TTL expiry / per-id isolation.

**Architecture:** `Microsoft.AspNetCore.OutputCaching` (shared framework, no new NuGet dep, AOT-clean). Register named policies (`OrderById` for both templates, `CustomerById` for vs only). Apply `.CacheOutput("policy")` to GET endpoints after `.RequireAuthorization(...)`. Inject `IOutputCacheStore` into write endpoints and call `EvictByTagAsync("orders" | "customers")` on successful mutations. Read TTL from `OutputCache:OrderByIdTtlSeconds` / `OutputCache:CustomerByIdTtlSeconds` config (default 30 seconds).

**Tech Stack:** .NET 10, ASP.NET Core OutputCaching (in shared framework), xUnit + WebApplicationFactory<Program> for tests, release-please for versioning.

**Design reference:** [docs/plans/2026-06-08-output-caching-design.md](2026-06-08-output-caching-design.md) (commit `c0c6eb8`)
**Branch:** `feat/189-output-caching` off `main` at `3b85678`.

## Test strategy

For each cache assertion, the test issues HTTP requests + a **side-channel SQL UPDATE** through the test factory's DI-resolved `IAsyncDbConnection`. The observable body content proves cache hit vs miss:

- **Cache HIT proof:** GET → SQL-update the row → GET; the second GET still returns the old body (cache served).
- **Cache MISS proof (after eviction):** GET → POST that triggers tag eviction → SQL-update the row → GET; the second GET returns the new body.

This avoids DI decoration, response header instrumentation, or timing heuristics. Works identically on both templates.

---

## Preflight

```powershell
git status
git log --oneline -2
```

Expected: clean tree on `feat/189-output-caching`, top commit `c0c6eb8 docs(design): output caching for read-by-id endpoints (#189)`.

SDK pin: relax `global.json` to `{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}` if local SDK doesn't match the pinned 10.0.300. **NEVER commit any global.json change.**

All paths below are relative to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Templates`.

---

## Task 1: za-clean Program.cs OutputCache registration + appsettings

**Goal:** Wire OutputCache services + middleware. Read TTL from config.

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/Program.cs`
- Modify: `content/za-clean/src/MyApp.Api/appsettings.json`

**Step 1: Add the policy registration**

In `content/za-clean/src/MyApp.Api/Program.cs`, BEFORE `var app = builder.Build();`, add (between the JSON config block and `var app = builder.Build();`):

```csharp
// ---------------------------------------------------------------------------
// Output caching — absorbs concurrent same-id reads to relieve Npgsql pool
// pressure under load (see docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md).
// TTL is config-tunable so adopters trade freshness vs. pool-relief without
// recompiling. Tag-based eviction: any write to /orders evicts ALL cached
// /orders/{id} responses. Conservative — always correct, simpler than per-id
// invalidation; a real production app might prefer `Tag($"order:{id}")` for
// precision but the simpler form is enough for the educational reference.
// ---------------------------------------------------------------------------
var orderByIdTtl = builder.Configuration.GetValue<int?>("OutputCache:OrderByIdTtlSeconds") ?? 30;
builder.Services.AddOutputCache(opt =>
{
    opt.AddPolicy("OrderById", b => b.Tag("orders").Expire(TimeSpan.FromSeconds(orderByIdTtl)));
});
```

In the middleware chain, AFTER `app.UseAuthorization();` and BEFORE `app.MapOrders();`, add:

```csharp
app.UseOutputCache();
```

**Step 2: Add the config key**

In `content/za-clean/src/MyApp.Api/appsettings.json`, add a top-level `OutputCache` section:

```json
{
  "Logging": { /* unchanged */ },
  "ConnectionStrings": { /* unchanged */ },
  "Shipping": { /* unchanged */ },
  "Jwt": { /* unchanged */ },
  "OutputCache": {
    "OrderByIdTtlSeconds": 30
  }
}
```

(Leave existing keys untouched. Add the new key as the last top-level property.)

**Step 3: Build the SUT and confirm clean**

```powershell
dotnet build content/za-clean/src/MyApp.Api -c Release -v minimal 2>&1 | Select-String -Pattern "error" -SimpleMatch | Select-Object -First 5
```

Expected: 0 errors.

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git status
git add `
  content/za-clean/src/MyApp.Api/Program.cs `
  content/za-clean/src/MyApp.Api/appsettings.json
git commit -m @'
feat(za-clean): wire OutputCaching services + middleware (#189)

Registers an `OrderById` policy (tag "orders", TTL from
OutputCache:OrderByIdTtlSeconds, default 30s) and inserts
app.UseOutputCache() between UseAuthorization and MapOrders.

No endpoints annotated yet — that's the next commit. This task is the
DI + middleware wiring only; build remains clean and CI green.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

(`Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` is the established commit pattern — see `git log -5 --format=%B`.)

---

## Task 2: za-clean endpoint annotations (GET cache + POST eviction)

**Goal:** Apply `.CacheOutput("OrderById")` to GET and call `EvictByTagAsync("orders", ct)` after successful POST.

**Files:**
- Modify: `content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs`

**Step 1: Add the cache annotation + injection**

Read the current endpoint:

```powershell
Get-Content content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs
```

The file currently has:
```csharp
using MyApp.Api.Dtos;
using MyApp.Api.Mappings;
using MyApp.Application.GetOrderById;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;

namespace MyApp.Api.Endpoints;
```

Add to the using block:
```csharp
using Microsoft.AspNetCore.OutputCaching;
```

In the POST handler signature, add an `IOutputCacheStore cache` parameter alongside the existing `IMediator mediator, CancellationToken ct`:

```csharp
group.MapPost("/", async (OrderRequest req, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
{
    var command = OrderRequestToCommand.Map(req);
    var result = await mediator.Send(command, ct).ConfigureAwait(false);
    if (result.IsSuccess)
    {
        // Evict all cached /orders/{id} responses so the next read picks up
        // the new state. Tag-based bulk eviction is conservative — even an
        // unrelated id's cache entry gets dropped — but it's always correct
        // and dramatically simpler than per-id invalidation.
        await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);
        return Results.Created($"/orders/{result.Value.Value}", new CreatedOrderResponse(result.Value));
    }
    return Results.Problem(result.Error.Message, statusCode: StatusCodes.Status400BadRequest);
}).RequireAuthorization("OrdersWrite");
```

In the GET handler, add `.CacheOutput("OrderById")` chained after `.RequireAuthorization("OrdersRead")`:

```csharp
group.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new GetOrderByIdQuery(new OrderId(id)), ct).ConfigureAwait(false);
    return result.IsSuccess
        ? Results.Ok(ReadModelToResponse.Map(result.Value))
        : Results.NotFound();
}).RequireAuthorization("OrdersRead").CacheOutput("OrderById");
```

**Step 2: Build**

```powershell
dotnet build content/za-clean/src/MyApp.Api -c Release -v minimal 2>&1 | Select-String -Pattern "error" -SimpleMatch | Select-Object -First 5
```

Expected: 0 errors. If `IOutputCacheStore` is unresolved, the using added in Step 1 was missed.

**Step 3: Run existing endpoint tests to confirm no regression**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~CreateOrderEndpointTests|FullyQualifiedName~AuthorizationTests" -v minimal
```

Expected: all green. Cache annotations don't change response shape on first-hit, so existing assertions still pass.

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git status
git add content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs
git commit -m @'
feat(za-clean): cache GET /orders/{id} + evict on POST (#189)

GET endpoint adds .CacheOutput("OrderById") to absorb concurrent
same-id reads. POST handler now also takes IOutputCacheStore and
evicts the "orders" tag after a successful create — conservative
bulk eviction, always correct.

Existing endpoint tests stay green; new cache-behaviour tests land
in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 3: za-clean cache-behaviour integration tests

**Goal:** Prove the cache works end-to-end via observable body changes.

**Files:**
- Create: `content/za-clean/tests/MyApp.IntegrationTests/OutputCacheTests.cs`

**Step 1: Read the existing factory to understand the test surface**

```powershell
Get-Content content/za-clean/tests/MyApp.IntegrationTests/MyAppFactory.cs
```

Understand: how the in-memory SQLite is wired, how seeding works, whether the factory exposes the connection for direct SQL.

**Step 2: Write the 4 failing tests**

Create `content/za-clean/tests/MyApp.IntegrationTests/OutputCacheTests.cs` with this content. Adjust the SQL UPDATE statement to match the actual schema (the `Orders` table has `CustomerId` per the design doc — verify via the test factory's seed code):

```csharp
using System.Data.Async;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// Cache behavior tests for the OutputCache layer (#189). Each test uses a
/// side-channel SQL UPDATE through the factory's IAsyncDbConnection to mutate
/// the underlying row, then re-issues the GET to observe whether the cache
/// served the stale body (HIT) or read the new body (MISS / evicted).
///
/// These tests use a custom factory per test (not IClassFixture) so the cache
/// state is isolated per case — the default in-memory cache is per-factory
/// scope.
/// </summary>
public sealed class OutputCacheTests
{
    private static MyAppFactory CreateFactory(int ttlSeconds = 30) =>
        new MyAppFactoryWithCacheTtl(ttlSeconds);

    [Fact]
    public async Task GET_orders_id_serves_cached_body_when_called_twice()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        // Seed an order via POST /orders so we have a stable id.
        var seededId = await SeedOneOrderAsync(client);

        // First GET — populates the cache.
        var first = await client.GetAsync($"/orders/{seededId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        // Side-channel SQL UPDATE to mutate the row directly.
        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        // Second GET — should still serve the OLD body from cache.
        var second = await client.GetAsync($"/orders/{seededId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);
        Assert.DoesNotContain("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task POST_orders_evicts_cached_GET_responses_so_next_read_is_fresh()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var seededId = await SeedOneOrderAsync(client);

        // First GET — populates cache.
        var first = await client.GetAsync($"/orders/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        // Side-channel update to make the new read distinguishable.
        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        // A POST to /orders evicts ALL "orders"-tagged cache entries.
        await SeedOneOrderAsync(client);

        // GET after the POST — should serve the NEW body (cache evicted).
        var second = await client.GetAsync($"/orders/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task GET_orders_id_serves_fresh_body_after_TTL_expiry()
    {
        // Force TTL down to 1 second for this test only.
        using var factory = CreateFactory(ttlSeconds: 1);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var seededId = await SeedOneOrderAsync(client);
        var first = await client.GetAsync($"/orders/{seededId}");
        var firstBody = await first.Content.ReadAsStringAsync();

        await UpdateOrderCustomerIdAsync(factory, seededId, newCustomerId: 999);

        // Wait past TTL — give some slack so we're not racing the timer.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var second = await client.GetAsync($"/orders/{seededId}");
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.NotEqual(firstBody, secondBody);
        Assert.Contains("\"customerId\":999", secondBody);
    }

    [Fact]
    public async Task GET_orders_with_different_ids_do_not_share_cache_entries()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.read", "orders.write"]));

        var id1 = await SeedOneOrderAsync(client, customerId: 1);
        var id2 = await SeedOneOrderAsync(client, customerId: 2);

        var resp1 = await client.GetAsync($"/orders/{id1}");
        var resp2 = await client.GetAsync($"/orders/{id2}");
        var body1 = await resp1.Content.ReadAsStringAsync();
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Contains("\"customerId\":1", body1);
        Assert.Contains("\"customerId\":2", body2);
        Assert.NotEqual(body1, body2);
    }

    // ----- helpers -----

    private static async Task<int> SeedOneOrderAsync(HttpClient client, int customerId = 42)
    {
        // POST /orders returns 201 with Location: /orders/{id}; extract id from there.
        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId,
            items = new[] { new { sku = "SKU-A", quantity = 1, unitPriceEur = 10m } },
            shippingZip = "1011AA",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var location = resp.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("POST /orders did not return a Location header");
        return int.Parse(location.Split('/')[^1]);
    }

    private static async Task UpdateOrderCustomerIdAsync(MyAppFactory factory, int orderId, int newCustomerId)
    {
        // Side-channel SQL through the factory's DI to mutate the row directly
        // without going through the API (which would itself evict the cache).
        using var scope = factory.Services.CreateScope();
        var conn = scope.ServiceProvider.GetRequiredService<IAsyncDbConnection>();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync().ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Orders\" SET \"CustomerId\" = @cid WHERE \"Id\" = @id";
        var pCid = cmd.CreateParameter();
        pCid.ParameterName = "@cid";
        pCid.Value = newCustomerId;
        cmd.Parameters.Add(pCid);
        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        pId.Value = orderId;
        cmd.Parameters.Add(pId);
        var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        Assert.Equal(1, affected);
    }
}

/// <summary>
/// Factory variant that overrides OutputCache:OrderByIdTtlSeconds for the
/// TTL-expiry test. Reuses MyAppFactory's connection/seed wiring otherwise.
/// </summary>
internal sealed class MyAppFactoryWithCacheTtl : MyAppFactory
{
    private readonly int _ttl;
    public MyAppFactoryWithCacheTtl(int ttlSeconds) { _ttl = ttlSeconds; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("OutputCache:OrderByIdTtlSeconds", _ttl.ToString()),
        }));
        base.ConfigureWebHost(builder);
    }
}
```

**Notes on adapting to the actual factory:**
- If `MyAppFactory` is not directly subclassable (e.g. it's sealed or its `ConfigureWebHost` is private), refactor it to allow the override.
- If the response JSON uses different property names than `customerId` (camelCase), update the `Assert.Contains("\"customerId\":1", body)` substrings to match the actual JSON shape.
- If POST /orders requires a different body shape than `{customerId, items, shippingZip}`, look at the existing `CreateOrderEndpointTests.cs` for the canonical shape and copy it.

**Step 3: Run the tests, confirm they pass**

```powershell
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~OutputCacheTests" -v minimal
```

Expected: **4 passed, 0 failed**.

If any test fails, the failure mode is the cache-behavior signal — read it carefully:
- Hit test fails (body1 != body2 with customerId:999): cache is NOT serving from cache → annotation in Task 2 broken or middleware order wrong.
- Eviction test fails (body1 == body2): cache is NOT being evicted on POST → Task 2's `EvictByTagAsync` not firing OR evicting wrong tag.
- TTL test fails: TTL not honored OR test override of `OutputCache:OrderByIdTtlSeconds` didn't take effect.

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git status
git add content/za-clean/tests/MyApp.IntegrationTests/OutputCacheTests.cs
git commit -m @'
test(za-clean): integration tests for OutputCache hit/evict/TTL/isolation (#189)

Four facts proving cache behavior end-to-end via side-channel SQL
mutation:

  - GET twice serves the cached body (hit)
  - POST evicts the orders tag so next read picks up the new state
  - TTL expiry produces a fresh read after the configured window
  - Different ids cache separately

Tests use a per-test MyAppFactoryWithCacheTtl override for the TTL
case; other tests use the default 30s TTL.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 4: za-vertical-slice Program.cs + appsettings

**Goal:** Mirror Task 1 for vs, plus register a second `CustomerById` policy.

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Program.cs`
- Modify: `content/za-vertical-slice/src/MyApp/appsettings.json`

**Step 1: Add policy registration + middleware**

In `content/za-vertical-slice/src/MyApp/Program.cs`, BEFORE `var app = builder.Build();`, add a block (place it after the JSON config block, before `app.UseAuthentication();`):

```csharp
// ---------------------------------------------------------------------------
// Output caching — see content/za-vertical-slice/README.md "Output caching"
// section and docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md for the
// finding that motivated this layer. Two policies: OrderById (tag "orders",
// evicted by PlaceOrder + CancelOrder) and CustomerById (tag "customers",
// evicted by CreateCustomer). TTLs config-tunable.
// ---------------------------------------------------------------------------
var orderByIdTtl = builder.Configuration.GetValue<int?>("OutputCache:OrderByIdTtlSeconds") ?? 30;
var customerByIdTtl = builder.Configuration.GetValue<int?>("OutputCache:CustomerByIdTtlSeconds") ?? 30;
builder.Services.AddOutputCache(opt =>
{
    opt.AddPolicy("OrderById", b => b.Tag("orders").Expire(TimeSpan.FromSeconds(orderByIdTtl)));
    opt.AddPolicy("CustomerById", b => b.Tag("customers").Expire(TimeSpan.FromSeconds(customerByIdTtl)));
});
```

After `app.UseAuthorization();` and before the endpoint Maps (`PlaceOrderEndpoint.Map(app);` etc.), add:

```csharp
app.UseOutputCache();
```

**Step 2: Add the config keys**

In `content/za-vertical-slice/src/MyApp/appsettings.json`, add a top-level `OutputCache` section:

```json
"OutputCache": {
  "OrderByIdTtlSeconds": 30,
  "CustomerByIdTtlSeconds": 30
}
```

(Place after existing top-level keys, preserving JSON structure.)

**Step 3: Build**

```powershell
dotnet build content/za-vertical-slice/src/MyApp -c Release -v minimal 2>&1 | Select-String -Pattern "error" -SimpleMatch | Select-Object -First 5
```

Expected: 0 errors.

**Step 4: Commit**

```powershell
git restore global.json # if relaxed
git status
git add `
  content/za-vertical-slice/src/MyApp/Program.cs `
  content/za-vertical-slice/src/MyApp/appsettings.json
git commit -m @'
feat(za-vertical-slice): wire OutputCaching services + middleware (#189)

Mirrors za-clean. Registers two policies — OrderById (tag "orders")
and CustomerById (tag "customers") — each with config-tunable TTL
defaulting to 30s. Inserts app.UseOutputCache() between
UseAuthorization and the endpoint Map calls.

No endpoints annotated yet — that's the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: za-vertical-slice endpoint annotations (GET cache + write eviction)

**Goal:** Annotate the GET endpoints and inject `IOutputCacheStore` into the write endpoints across Orders + Customers features.

**Files:**
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Orders/CancelOrder/CancelOrder.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Customers/GetCustomer/GetCustomer.cs`
- Modify: `content/za-vertical-slice/src/MyApp/Features/Customers/CreateCustomer/CreateCustomer.cs`

**Step 1: GetOrder — annotate GET**

In `content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs`, add to the using block:

```csharp
using Microsoft.AspNetCore.OutputCaching;
```

In the endpoint Map method, chain `.CacheOutput("OrderById")` after `.RequireAuthorization("OrdersRead")`:

```csharp
public static class GetOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{id:int}", static async (int id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetOrderQuery(new OrderId(id)), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersRead")
            .CacheOutput("OrderById");
}
```

**Step 2: PlaceOrder — inject cache + evict on success**

In `content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs`, modify the endpoint to take `IOutputCacheStore cache` and evict on success. The lambda was previously `static` — keep it `static` since the cache is a DI parameter, not closed over.

```csharp
using Microsoft.AspNetCore.OutputCaching;
// ... existing usings ...

public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    // Tag-based bulk eviction — drops all cached /orders/{id}
                    // entries. See content/za-vertical-slice/README.md
                    // "Output caching" section for the trade-off rationale.
                    await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);
                    return Results.Created($"/orders/{result.Value.Value}", result.Value);
                }
                return result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}
```

(The exact existing endpoint body may differ — preserve its prior logic, just add the `IOutputCacheStore cache` param + the conditional `EvictByTagAsync` call on success. The `Results.Created(...)` line shape may already be different; don't break it.)

**Step 3: CancelOrder — same eviction pattern**

In `content/za-vertical-slice/src/MyApp/Features/Orders/CancelOrder/CancelOrder.cs`, add the `IOutputCacheStore cache` parameter to the endpoint lambda and call `await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);` after a successful cancel. Preserve the existing endpoint's `RequireAuthorization` chain.

**Step 4: GetCustomer — annotate GET**

In `content/za-vertical-slice/src/MyApp/Features/Customers/GetCustomer/GetCustomer.cs`, add the using and chain `.CacheOutput("CustomerById")` after the existing `.RequireAuthorization("CustomersRead")`.

**Step 5: CreateCustomer — evict on success**

In `content/za-vertical-slice/src/MyApp/Features/Customers/CreateCustomer/CreateCustomer.cs`, add `IOutputCacheStore cache` to the endpoint lambda and call `await cache.EvictByTagAsync("customers", ct).ConfigureAwait(false);` after a successful create.

**Step 6: Build all changes together**

```powershell
dotnet build content/za-vertical-slice/src/MyApp -c Release -v minimal 2>&1 | Select-String -Pattern "error" -SimpleMatch | Select-Object -First 5
```

Expected: 0 errors.

**Step 7: Run existing endpoint tests**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
```

Expected: all existing tests pass (cache annotations don't change first-hit response shape).

**Step 8: Commit**

```powershell
git restore global.json # if relaxed
git status
git add `
  content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs `
  content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs `
  content/za-vertical-slice/src/MyApp/Features/Orders/CancelOrder/CancelOrder.cs `
  content/za-vertical-slice/src/MyApp/Features/Customers/GetCustomer/GetCustomer.cs `
  content/za-vertical-slice/src/MyApp/Features/Customers/CreateCustomer/CreateCustomer.cs
git commit -m @'
feat(za-vertical-slice): cache GET-by-id + evict on writes (#189)

GET /orders/{id} and GET /customers/{id} now use the corresponding
named OutputCache policy. PlaceOrder, CancelOrder, and CreateCustomer
inject IOutputCacheStore and evict the matching tag on success.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 6: za-vertical-slice cache-behaviour integration tests

**Goal:** Mirror Task 3 for vs — 4 facts on Orders, plus 4 parallel facts on Customers (8 tests total in this file).

**Files:**
- Create: `content/za-vertical-slice/tests/MyApp.IntegrationTests/OutputCacheTests.cs`

Use the same test pattern as Task 3 (factory + side-channel SQL UPDATE + observable body changes), with:
- 4 facts for `/orders/{id}` (hit, eviction on PlaceOrder POST, TTL expiry, per-id isolation)
- 4 facts for `/customers/{id}` (same shape, eviction on CreateCustomer POST)

The vs factory may have a different shape than za-clean's — read its `MyAppFactory.cs` first. The SQL UPDATE statement may target a different column (e.g. `"Total"` instead of `"CustomerId"` if the GET response includes Total but the schema doesn't have CustomerId — verify against the actual GetOrder DTO).

**Step 1: Read the vs factory + GET response DTO**

```powershell
Get-Content content/za-vertical-slice/tests/MyApp.IntegrationTests/MyAppFactory.cs
Get-Content content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs | Select-String -Pattern "record|sealed class" -Context 0,3
```

Pick a column that's both serialized in the GET response AND simple to UPDATE (likely `"Total"` for orders since the DTO is `(OrderId Id, CustomerId CustomerId, decimal Total)`, or `"Name"` for customers).

**Step 2: Write the 8 tests**

Create `content/za-vertical-slice/tests/MyApp.IntegrationTests/OutputCacheTests.cs` mirroring Task 3 but with two `[Fact]` sets — one for Orders, one for Customers. Each uses a side-channel SQL UPDATE on whichever column is most observable in the GET response body. Run the same hit / eviction-on-write / TTL / per-id pattern for each entity.

**Step 3: Run + commit**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj `
  --filter "FullyQualifiedName~OutputCacheTests" -v minimal
```

Expected: **8 passed**.

```powershell
git add content/za-vertical-slice/tests/MyApp.IntegrationTests/OutputCacheTests.cs
git commit -m @'
test(za-vertical-slice): integration tests for OutputCache (#189)

Mirrors za-clean's OutputCacheTests with two entity sets: Orders
(GET /orders/{id} + POST /orders eviction) and Customers (GET
/customers/{id} + POST /customers eviction). Four facts each — hit,
eviction, TTL expiry, per-id isolation.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 7: README + benchmark doc updates

**Goal:** Document the cache layer in both template READMEs and link this PR from the #189 finding doc.

**Files:**
- Modify: `content/za-clean/README.md`
- Modify: `content/za-vertical-slice/README.md`
- Modify: `docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md`

**Step 1: za-clean README — add an "Output caching" section**

In `content/za-clean/README.md`, find a sensible section break (likely near the existing "Authorization" or "Database" section). Add a new section like:

```markdown
## Output caching

`GET /orders/{id}` is wrapped in ASP.NET Core OutputCaching with a configurable TTL (default 30 seconds). Concurrent same-id reads are served from the in-memory cache, absorbing pressure on the Npgsql connection pool under load — see `docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md` for the empirical investigation that motivated this layer.

**Tuning:**

```json
{
  "OutputCache": {
    "OrderByIdTtlSeconds": 30
  }
}
```

Lower TTL = fresher data + less pool relief; higher = more pool relief + staler responses.

**Eviction:** any successful POST `/orders` evicts ALL cached `/orders/{id}` responses via tag-based bulk eviction. Conservative (always correct, slightly wasteful in cache utilization) — for production-grade precision a per-id eviction (`EvictByTagAsync($"order:{id}")`) would be more efficient but adds complexity.

**Distributed caching:** the default cache is per-process in-memory. Multi-instance deployments need a distributed backing store (Redis is the typical choice via `Microsoft.Extensions.Caching.StackExchangeRedis` + `AddStackExchangeRedisOutputCache`). Out of scope for this template.
```

**Step 2: za-vs README — same section, both entities**

Add the equivalent section to `content/za-vertical-slice/README.md`, mentioning both `/orders/{id}` and `/customers/{id}`, both config keys, and both eviction triggers (PlaceOrder + CancelOrder for orders, CreateCustomer for customers).

**Step 3: Update #189 finding doc**

Append a "Response" section to `docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md`:

```markdown
## Response — OutputCaching shipped in v0.15.0

The findings above led to PR #196 (or whichever number lands) which adds ASP.NET Core OutputCaching to both templates' GET-by-id endpoints with tag-based eviction on writes. Cache hits skip the Npgsql round-trip entirely, absorbing the pool pressure that motivated this investigation for the common "many concurrent reads of the same id" workload.

The structural question — why does vs hit the pool harder than clean at the same workload — remains open as a deferred investigation. The hypothesis is that vs's `IAsyncDbConnection`-direct DI shape holds connections marginally longer than clean's `IOrderRepository`-wrapped pattern, but a connection-lifecycle micro-bench wasn't built; if a real-world workload surfaces where caching isn't enough, reopen the question.
```

**Step 4: Commit**

```powershell
git add `
  content/za-clean/README.md `
  content/za-vertical-slice/README.md `
  docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md
git commit -m @'
docs: document OutputCaching in both templates + link from #189 finding

Each template README gains a self-contained "Output caching" section
explaining what the layer does, how to tune TTL, how tag-based
eviction works, and the trade-off vs distributed caching. The #189
finding doc links to this PR as the in-template response.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 8: Full-suite sweep + push + PR + admin-merge + #189 closing

**Goal:** Verify both templates green, ship the PR, cut v0.15.0, close #189.

**Files:** none. Verification + git/gh operations.

**Step 1: Full sweep za-clean**

```powershell
dotnet test content/za-clean/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-clean/tests/MyApp.ArchitectureTests/MyApp.ArchitectureTests.csproj -v minimal
```

Expected: all green. New OutputCacheTests should add 4 to IntegrationTests count.

**Step 2: Full sweep za-vs**

```powershell
dotnet test content/za-vertical-slice/tests/MyApp.UnitTests/MyApp.UnitTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.IntegrationTests/MyApp.IntegrationTests.csproj -v minimal
dotnet test content/za-vertical-slice/tests/MyApp.ConventionTests/MyApp.ConventionTests.csproj -v minimal
```

Expected: all green. New OutputCacheTests adds 8 to IntegrationTests count.

If anything fails, STOP and report exactly which tests failed.

**Step 3: Restore global.json if relaxed**

```powershell
git status
git restore global.json # if needed
```

Working tree should match the committed state of the branch.

**Step 4: Push**

```powershell
git log --oneline 3b85678..HEAD # should show ~8 commits on this branch
git push -u origin feat/189-output-caching
```

**Step 5: Open the PR**

```powershell
gh pr create --title "feat(templates): output caching for GET-by-id endpoints (#189)" --body @'
## Summary

Closes #189. Adds ASP.NET Core OutputCaching to both templates'' GET-by-id endpoints as the read-path defensive measure for the Npgsql pool-exhaustion finding identified in [the #189 dotnet-counters investigation](../blob/main/docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md).

## What changed

Both templates symmetrically:

- **Named policies in `Program.cs`** — `OrderById` (clean+vs) and `CustomerById` (vs only), each with configurable TTL via `OutputCache:*TtlSeconds` config (default 30 seconds), tag-based eviction.
- **GET-by-id annotated** — `.CacheOutput("OrderById")` chained after `.RequireAuthorization("OrdersRead")` on the GET endpoints. Same for `CustomerById` in vs.
- **Write endpoints inject `IOutputCacheStore` and call `EvictByTagAsync(...)`** on successful mutations. Conservative bulk eviction (always correct, simpler than per-id invalidation).
- **Integration tests** (4 facts per entity per template = 4 + 8 = 12 new tests) — cache hit, eviction on write, TTL expiry with override factory, per-id cache isolation. Tests use side-channel SQL UPDATEs to mutate rows directly so observable body differences prove cache state.
- **README sections** in both templates documenting what the cache does, how to tune TTL, how eviction works, distributed-cache considerations.

## What this does NOT do

- **No distributed cache backend.** Default in-memory; multi-instance deployments need a separate Redis/etc. backing — documented in the README sections.
- **No connection-lifecycle micro-bench.** The structural question "why does vs hit the pool harder than clean at the same workload" remains open as a deferred follow-up. The hypothesis is that vs''s `IAsyncDbConnection`-direct DI shape holds connections marginally longer than clean''s `IOrderRepository`-wrapped pattern, but caching addresses the practical impact regardless of root cause.
- **No per-id eviction.** Tag-based bulk eviction drops all entries for a given entity on any write to that entity. Documented as a refinement candidate.

## Test plan

- [x] za-clean OutputCacheTests — 4 facts pass (hit / eviction / TTL / per-id)
- [x] za-vs OutputCacheTests — 8 facts pass (4 × Orders + 4 × Customers)
- [x] All existing tests stay green in both templates
- [x] AOT publish smoke (`aot-publish-smoke` + `aot-publish-smoke-vs` CI checks) — OutputCaching is shared-framework + AOT-clean on .NET 8+

## Approach

Approach A from the brainstorm (output caching only + deferred structural-cause investigation). Design at `docs/plans/2026-06-08-output-caching-design.md`. Rejected alternatives: connection-lifecycle micro-bench first (5-6 hours for an actionable outcome that converges on "enable output caching" anyway), or investigation-only with no fix.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

Capture the PR number.

**Step 6: Watch CI**

```powershell
gh pr checks --watch
```

Expected: all 6 checks green (`build`, `build-vs`, `real-run-smoke`, `real-run-smoke-vs`, `aot-publish-smoke`, `aot-publish-smoke-vs`).

If a check fails, STOP and report. AOT smoke check is the load-bearing one for the OutputCache AOT-compatibility claim.

**Step 7: Admin-merge with `feat:` squash**

```powershell
$prNumber = gh pr view --json number -q .number
gh pr merge $prNumber --squash --admin --subject "feat(templates): output caching for GET-by-id endpoints (#$prNumber)"
```

`feat:` maps to minor bump per the validated memory note — release-please will open a `chore(main): release ZeroAlloc.Templates 0.15.0` PR.

**Step 8: Monitor release-please**

```powershell
for ($i = 0; $i -lt 8; $i++) {
    $rp = gh pr list --label autorelease:pending --json number,title -q '.[0]'
    if ($rp) { Write-Host "Release-please PR: $rp"; break }
    Start-Sleep -Seconds 15
}
```

Expected: a `chore(main): release ZeroAlloc.Templates 0.15.0` PR within ~60-120s. Capture its number.

**Step 9: Close #189 with finding-of-record comment**

```powershell
gh issue comment 189 --body @'
Closing — output caching shipped to both templates in PR #<this PR number> (v0.15.0).

**What we proved** (via `dotnet-counters` under sustained load): Npgsql connection pool exhaustion is the dominant load-coupled bottleneck — `used` connections peg at the 500-pool ceiling with mean 2,000-4,000 pending requests in the wait queue. Postgres itself is fine.

**What we shipped:** ASP.NET Core OutputCaching on the GET-by-id endpoints in both templates with tag-based eviction on writes and configurable TTL. Cache hits skip the Npgsql roundtrip entirely, absorbing the pool pressure for the common "many concurrent reads of the same id" workload.

**What we deferred:** the structural question of *why* za-vs hits the pool harder than za-clean at the same workload remains open. Hypothesis: vs''s `IAsyncDbConnection`-direct DI shape holds connections marginally longer than clean''s `IOrderRepository`-wrapped pattern. A connection-lifecycle micro-bench would settle this but its actionable outcome would converge on either (a) "enable output caching" — already shipped — or (b) a non-trivial refactor of vs''s scope semantics. Reopen if a real-world workload surfaces where caching isn''t enough.
'@
gh issue close 189
```

**Step 10: Final state**

```powershell
git fetch origin main
git log origin/main --oneline -3
gh pr view $prNumber --json mergeCommit -q .mergeCommit.oid
gh issue view 189 --json state -q .state
```

Confirm:
- PR merged
- main top commit is the feature
- #189 state is CLOSED with the closing comment posted
- release-please PR for v0.15.0 is open (awaiting your merge — that's the user's call)

**Step 11: DO NOT merge the release-please PR.** That's the user's call. Templates auto-publish to NuGet from the release-please workflow when the release PR merges.

## Report (Task 8 only) ≤350 words

1. Test counts (za-clean + za-vs, each project's pass count)
2. PR number opened
3. CI check summary
4. Squash-merge SHA on main
5. Release-please PR number + title (confirm `0.15.0` minor bump)
6. #189 closed?
7. Anything unexpected

---

## Notes

- **Each task ships independently.** 7 source commits + 1 verification commit pattern (Task 8 has no commit of its own).
- **TDD discipline:** Tasks 3 and 6 write tests against the just-landed cache annotations. The tests would fail without the cache (no observable difference between first and second GET because both go to the repo), and pass after the cache annotations from Tasks 2 and 5 land. The "red" phase is implicit in the task ordering.
- **`feat:` prefix on the squash → v0.15.0 minor.** Per the empirical mapping memory, `feat:` is the only prefix that bumps minor; `fix:` / `perf:` / `docs:` / `refactor:` bump patch; `chore:` doesn't bump at all.
- **Per-task subagent context:** each task's prompt should include enough of the surrounding code (the relevant file's current state) so the implementer doesn't need to re-explore.
- **The test factories may need refactoring** to make `ConfigureWebHost` overridable. If the existing `MyAppFactory` is sealed or its config is private, the Task 3 subagent must adjust the factory's accessibility before adding the TTL override.
