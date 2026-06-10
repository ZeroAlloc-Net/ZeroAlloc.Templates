# MyApp

Vertical Slice Architecture Web API. Source-generated, AOT-friendly, zero-allocation through the framework hot path. Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

Where Clean Architecture splits the codebase by *technical layer* (Domain / Application / Infrastructure / Api, four csprojs, dependency direction strictly inward), vertical slice splits by *use case*: one folder per feature, one file per slice, every slice owns its request + validator + handler + endpoint + entity. The 10-package ZeroAlloc showcase is identical; the wiring is inverted.

| | Value |
|---|---:|
| **AOT binary size** | ~27 MB (`MyApp.exe`) + ~2 MB native deps (win-x64, self-contained) |
| **AOT cold start** | ~540 ms (process → `/healthz` 200, best of 3 on warm disk) |
| **ValueObject `TryCreate` (Money)** | ~3 ns / 0 B |
| **TypedId construct** | ~0 ns / 0 B (inlined away) |
| **Validator (source-generated)** | ~4 ns / 0 B |
| **`Result<T, Error>` (success path)** | ~7 ns / 0 B |
| **WritePipeline (ASP.NET + ZA.ORM, Postgres)** | ~659 μs / 32 KB per request |
| **WritePipeline (ASP.NET + ZA.ORM, Sqlite)** | ~186 μs / 29 KB per request |

AOT figures measured on i9-12900HK / Windows 11 / .NET 10.0.8 post-B5 (PR #161 — the swap from JIT to NativeAOT). Pipeline + primitive numbers measured in CI on Ubuntu 24.04 / AMD EPYC / .NET 10.0.8 via [`Benchmarks (manual)` run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747).

**Reproduce:**

```bash
# Framework primitives (zero-alloc, ns/op — what ZA is)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks.Primitives -- --filter "*"

# Full pipeline (ASP.NET + ZA.ORM — what the platform costs)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks -- --filter "*WritePipelineBench*"

# AOT publish + cold-start
dotnet publish src/MyApp -c Release -r win-x64 -o ./aot-out
time ./aot-out/MyApp  # measure to /healthz
```

## Quickstart

```bash
dotnet run --project src/MyApp
# In another shell:
curl http://localhost:5000/healthz
# → {"status":"ok"}
```

The API boots, applies its ZA.ORM-managed embedded SQL migrations, and listens on the Kestrel default. OpenTelemetry traces stream to the console.

> Pipeline + primitive numbers measured in CI on Ubuntu 24.04 / AMD EPYC / .NET 10.0.8 via [`Benchmarks (manual)` run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747). NBomber-Postgres at 5,000-RPS open-model inject sustains 4,312 RPS / 0 failures (p50 37 ms, p99 1,319 ms; full table in [docs/za-vertical-slice.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/za-vertical-slice.md#load-testing-against-postgres)).

> ⚠️ **Regression-net numbers, not capacity.** The CI bench co-locates load
> generator + SUT + Postgres on the same runner. See
> [docs/benchmarks/README.md](../../docs/benchmarks/README.md) for context and
> [docs/benchmarks/capacity-recipe.md](../../docs/benchmarks/capacity-recipe.md)
> for the decoupled recipe.

## Layout

```
src/
└── MyApp/                                One assembly, everything inside it.
    ├── Program.cs                        DI wiring + endpoint-discovery walk
    ├── Common/                           Shared primitives: TypedIds, Errors, Telemetry
    ├── Authorization/Policies.cs         [Policy] declarations
    ├── Persistence/                      IAsyncDbConnection wiring + Migrations/{Sqlite,Postgres}/*.sql
    └── Features/
        ├── Orders/
        │   ├── PlaceOrder/PlaceOrder.cs    request + validator + handler + endpoint + Order entity + [Command] partial
        │   ├── GetOrder/GetOrder.cs        request + validator + handler + endpoint + [Query] partial
        │   ├── ListOrders/ListOrders.cs    paged read ([Query] partial co-located)
        │   └── CancelOrder/CancelOrder.cs  state transition ([Command] partial co-located)
        └── Customers/
            ├── CreateCustomer/CreateCustomer.cs  owns Customer entity + [Command] partial
            └── GetCustomer/GetCustomer.cs        [Query] partial co-located

tests/
├── MyApp.UnitTests/         xUnit — handler-level unit tests, one folder per slice
├── MyApp.ConventionTests/   NetArchTest — vertical-slice conventions enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — endpoint roundtrips

benchmarks/
├── MyApp.Benchmarks.Primitives/  BenchmarkDotNet — ZA primitives in isolation (0 B framework cost)
├── MyApp.Benchmarks/             BenchmarkDotNet — full ASP.NET + ZA.ORM pipeline cost
└── MyApp.LoadTest/               NBomber — RPS under sustained concurrency
```

## The canonical slice — `Features/Orders/PlaceOrder/PlaceOrder.cs`

One file holds every concept that participates in `POST /orders`:

```csharp
// Request — the public contract dispatched through IMediator.
[RequirePolicy("customer")]
public readonly record struct PlaceOrderCommand(CustomerId CustomerId, decimal Total)
    : IRequest<Result<OrderId, Error>>;

// Validator — invoked automatically by .UseValidation() before the handler runs.
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.Total).GreaterThan(0).WithMessage("Total must be positive");
    }
}

// Handler — owns the DB work. Persistence is a [Command] partial generated by ZA.ORM,
// co-located in this file so the whole slice still reads top-to-bottom.
public sealed class PlaceOrderHandler(IAsyncDbConnection db)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(OrderId.New(), cmd.CustomerId, cmd.Total);
        await InsertOrderAsync(db, order, ct);
        return order.Id;
    }

    // ZA.ORM source-generates the body from this signature + the SQL template.
    [Command("INSERT INTO orders (id, customer_id, total) VALUES (@Id, @CustomerId, @Total)")]
    static partial ValueTask InsertOrderAsync(IAsyncDbConnection db, Order order, CancellationToken ct);
}

// Endpoint — picked up automatically by the assembly walk in Program.cs.
public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(cmd, ct);
            return result.Match(id => Results.Created($"/orders/{id}", id), err => err.ToProblem());
        });
}

// Persistence entity — owned by this slice. ZA.ORM materialises rows into it directly.
internal sealed class Order
{
    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public decimal Total { get; private set; }
    private Order() { }
    public Order(OrderId id, CustomerId customerId, decimal total) =>
        (Id, CustomerId, Total) = (id, customerId, total);
}
```

To add a new use case — copy a slice file, rename, tweak. No other place in the codebase needs editing: `Program.cs`'s assembly walk picks up the new `*Endpoint` class automatically, `services.AddMediator().RegisterHandlersFromAssembly(...)` picks up the new handler automatically, the `[RequirePolicy]` declaration is enforced by `.UseAuthorization()` automatically.

## Conventions enforced by `MyApp.ConventionTests`

- Every `*Command` / `*Query` implements `IRequest<>`.
- Every `*Handler` is `public sealed`.
- No slice references another slice's types (slices are independent — share via `Common/`).

Violations fail CI.

## Output caching

`GET /orders/{id}` and `GET /customers/{id}` are wrapped in ASP.NET Core OutputCaching with per-entity configurable TTL (defaults: 30 seconds each). Concurrent same-id reads are served from the in-memory cache, absorbing pressure on the Npgsql connection pool under load — see [docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md](../../docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md) for the empirical investigation that motivated this layer.

**Tuning:**

```json
{
  "OutputCache": {
    "OrderByIdTtlSeconds": 30,
    "CustomerByIdTtlSeconds": 30
  }
}
```

**Eviction:** writes evict the corresponding tag on success:
- `PlaceOrder` and `CancelOrder` → `EvictByTagAsync("orders")`
- `CreateCustomer` → `EvictByTagAsync("customers")`

Tag-based bulk eviction is conservative (always correct, simpler than per-id) — a production-grade app might prefer per-id eviction for precision.

**Authenticated GETs:** the framework's `DefaultPolicy` refuses to cache responses with an `Authorization` header. A custom `CacheAuthenticatedGetsPolicy` (in `MyApp/CacheAuthenticatedGetsPolicy.cs`) bypasses that check — safe here because the demo payloads are not user-specific. If you add an endpoint whose body varies per user, use `.CacheOutput(o => o.SetVaryByHeader("Authorization"))` to key per-token instead.

**Distributed caching:** the default cache is per-process in-memory. Multi-instance deployments need a distributed backing store (Redis is typical via `Microsoft.Extensions.Caching.StackExchangeRedis` + `AddStackExchangeRedisOutputCache`). Out of scope for this template.

## Extending

- **AI agents**: [AGENTS.md](AGENTS.md) — orientation for Claude Code, Cursor, GitHub Copilot, Codex, Aider. Includes "how to add a slice" recipes and the ZA-specific gotchas.
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. Startup runs ZA.ORM's `MigrationRunner` over the embedded SQL files under `src/MyApp/Persistence/Migrations/Postgres/` — no reflection, AOT-clean. After entity or schema changes, hand-author a new migration file under both providers:
  ```
  src/MyApp/Persistence/Migrations/Sqlite/002_add_customer_email.sql
  src/MyApp/Persistence/Migrations/Postgres/002_add_customer_email.sql
  ```
  File-naming convention: `NNN_description.sql` — a 3+ digit zero-padded version prefix (strictly increasing) plus a snake_case description. `MigrationRunner` orders by the prefix and applies anything newer than the recorded high-water mark on next startup.

## License

MIT — see [LICENSE](LICENSE).
