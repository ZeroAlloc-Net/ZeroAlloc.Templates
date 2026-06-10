# Output Caching for Read-by-Id Endpoints — Design

**Status:** approved 2026-06-08
**Scope:** Both templates. Read-path defensive measure for #189; good-practice infrastructure independent of #189.
**Target version:** ZA.Templates v0.15.0 (minor — adds a meaningful template feature; `feat:` squash).
**Closes:** [#189](https://github.com/ZeroAlloc-Net/Templates/issues/189) (with documented deferral of the structural connection-lifecycle question).
**Branch:** `feat/189-output-caching` off `main` at `3b85678`.

## Background

The #189 investigation (commit `3b85678` on `main`, findings doc `docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md`) identified **Npgsql connection pool exhaustion** as the dominant load-coupled bottleneck on the read path under sustained NBomber load. `db.client.connection.count[used]` pegs at the 500-pool ceiling on both 5k and 8k target runs; mean 2,000-4,000 pending requests sit in the wait queue. Postgres itself is fine (p99 query duration <1s) — the per-request latency premium lives in waiting for a connection.

The remaining open question is *why* za-vs hits the pool harder than za-clean at the same workload (same 500-connection pool, same SQL). The answer probably lives in connection-lifecycle differences between the two templates' DI shapes (`IAsyncDbConnection`-direct vs `IOrderRepository`-wrapped), but that investigation isn't load-bearing for shipping a fix — output caching addresses the symptom regardless of which template has the heavier lifecycle.

## Decision

Adopt **Approach A** from the brainstorm: add ASP.NET Core OutputCaching to both templates' GET-by-id endpoints, ship as `feat(templates): output caching for read-by-id endpoints`, close #189 with a finding-of-record comment, document the deferred structural question as a future investigation.

Rejected:
- **Approach B** (connection-lifecycle micro-bench first): ~5-6 hours for an actionable outcome that converges on "enable output caching" anyway. Time-cost not justified.
- **Approach C** (investigation only, no fix): worst of both worlds — same time investment, no shipped improvement to the templates.

## What changes

### za-clean (`MyApp.Api`)

1. **`content/za-clean/src/MyApp.Api/Program.cs`** — add OutputCache registration + middleware:
   ```csharp
   var orderByIdTtl = builder.Configuration.GetValue<int?>("OutputCache:OrderByIdTtlSeconds") ?? 30;
   builder.Services.AddOutputCache(opt =>
   {
       opt.AddPolicy("OrderById", b => b.Tag("orders").Expire(TimeSpan.FromSeconds(orderByIdTtl)));
   });
   // ... later ...
   app.UseAuthentication();
   app.UseAuthorization();
   app.UseOutputCache();
   ```
2. **`content/za-clean/src/MyApp.Api/Endpoints/OrdersEndpoints.cs`** — annotate GET:
   ```csharp
   group.MapGet("/{id:int}", async (...) => { ... })
        .RequireAuthorization("OrdersRead")
        .CacheOutput("OrderById");
   ```
   POST handler: accept `IOutputCacheStore cache` via DI, call `await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);` after successful create (after the mediator returns `IsSuccess`).
3. **`content/za-clean/src/MyApp.Api/appsettings.json`** — add `OutputCache:OrderByIdTtlSeconds: 30`. Same key for `appsettings.Development.json` if desired (defaults inherit otherwise).

### za-vertical-slice (`MyApp`)

1. **`content/za-vertical-slice/src/MyApp/Program.cs`** — same `AddOutputCache` + `UseOutputCache` wiring with both `OrderById` and `CustomerById` policies. TTL configured per entity.
2. **`content/za-vertical-slice/src/MyApp/Features/Orders/GetOrder/GetOrder.cs`** — endpoint Map adds `.CacheOutput("OrderById")`.
3. **`content/za-vertical-slice/src/MyApp/Features/Orders/PlaceOrder/PlaceOrder.cs`** — endpoint/handler receives `IOutputCacheStore`, evicts `"orders"` tag on success.
4. **`content/za-vertical-slice/src/MyApp/Features/Orders/CancelOrder/CancelOrder.cs`** — same eviction.
5. **`content/za-vertical-slice/src/MyApp/Features/Customers/GetCustomer/GetCustomer.cs`** — `.CacheOutput("CustomerById")` with `Tag("customers")`.
6. **`content/za-vertical-slice/src/MyApp/Features/Customers/CreateCustomer/CreateCustomer.cs`** — evicts `"customers"` tag on success.
7. **`content/za-vertical-slice/src/MyApp/appsettings.json`** — both TTL keys.

## Cache key + eviction strategy

- **Key:** ASP.NET Core's default cache key includes the request path + method, so `/orders/1` vs `/orders/2` cache separately automatically. No additional `VaryByValue` needed — the templates' demo orders aren't user-specific.
- **Tag-based eviction (entity-type-wide):** any write to orders evicts ALL order caches (`EvictByTagAsync("orders")`). Conservative — always correct, slightly wasteful in cache utilization. For a production app, per-id eviction (`EvictByTagAsync($"order:{id}")`) would be more efficient; documented as a future refinement.
- **Eviction order:** AFTER the write completes successfully (transaction committed). On validation/auth failure, nothing evicts.

## Testing

Use the existing `WebApplicationFactory<Program>` integration test pattern in both templates. Each test class gets its own factory instance (and therefore its own cache), so tests don't share cache state.

New tests for **both** templates (4 facts each):

1. **Cache hit** — GET twice; assert the repo's `GetByIdAsync` was called exactly once (use the existing `FakeOrderRepository` from unit tests for za-clean; vs needs a counted-call shim around its `[Query]` partial). Both responses are 200 with identical body.
2. **Cache eviction on write** — GET → POST `/orders` → GET; assert the repo was called twice.
3. **TTL expiry** — use a test-specific config that sets TTL to 1 second; GET, `Task.Delay(1500)`, GET; assert two repo calls.
4. **Different ids don't share** — GET `/orders/1`, GET `/orders/2`; assert two repo calls with the respective ids.

Existing tests must stay green — `AuthorizationTests`, `CreateOrderEndpointTests`, the vs feature endpoint tests. Output caching doesn't change the request/response shape on cache hits or misses.

## AOT compatibility

`Microsoft.AspNetCore.OutputCaching` is in the ASP.NET Core shared framework (no new NuGet dependency) and AOT-clean in .NET 8+. Both templates declare `PublishAot=true`. Confirmed via the existing `aot-publish-smoke` and `aot-publish-smoke-vs` CI checks — these will run on the PR.

## Documentation updates

- **Per-template README:** add a short "Output caching" section explaining what's enabled by default, how to tune TTL via config, how tag-based eviction works on writes, one-liner pointing at the #189 finding doc.
- **`docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md`:** append a closing section linking to this PR as the response to the pool-exhaustion finding.
- **#189 closing comment:** quote the finding (pool exhaustion), state the response (output caching shipped to both templates), explain the deferred structural-cause question (left for a future investigation if a non-cacheable workload surfaces).

## Versioning

- `feat(templates):` squash — adds a meaningful new template feature (configurable OutputCaching on read endpoints).
- release-please cuts **v0.15.0** (minor — `feat:` maps to minor per the empirical mapping memory).
- ZA.Templates auto-publishes to NuGet via release-please.yml on the release tag.

## What stays out of scope

- **Distributed cache (Redis).** Default in-memory only. Multi-instance deployments are a separate template concern, briefly noted in the README's caching section as future work.
- **Connection-lifecycle micro-bench.** Documented as deferred follow-up; would be reopened if a non-cacheable workload surfaces where caching isn't enough.
- **Per-id eviction precision.** Tag-eviction is conservative; per-id eviction documented as a refinement candidate.
- **Output caching on LIST endpoints** (`GET /orders` style). vs has `ListOrdersHandler` but isn't in #189's scope. Could be added in a follow-up if list traffic patterns warrant.
- **Cache stampede protection.** ASP.NET Core OutputCache doesn't natively coalesce concurrent cache-miss requests for the same key; for production-grade workloads with sharp cache-miss thundering-herd patterns, a single-flight wrapper would be needed. Documented as future work, but the default behavior is acceptable for the educational reference.

## Risk

- **Eviction-correctness scope creep.** If a future entity gets added (e.g. line items have their own GET endpoint), forgetting to evict on related writes silently serves stale data. Mitigation: README documents the tag-eviction discipline; future PR-author responsibility to add the right tag on new caching endpoints.
- **TTL default tuning.** 30s is a defensible default (refresh roughly every read burst). Educational reference, not a tuned production system; README explains the tradeoff (lower TTL = fresher data + less pool relief; higher = more pool relief + staler responses).
- **Test isolation.** OutputCache is registered as singleton (default in-memory store). Each `WebApplicationFactory<Program>` instance gets its own service provider, so tests in different test classes are isolated. Tests within a single class share cache state — order-of-execution dependencies are possible. Mitigation: each test that mutates cache state uses a unique id.
- **Per-template README footprint.** Adding a section adds ~30 lines per README. The "Output caching" section earns its keep by being a real feature with config knobs.
