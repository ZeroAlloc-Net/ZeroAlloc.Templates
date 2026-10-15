# MyApp

Vertical Slice Architecture Web API. Source-generated and published as Native AOT. The ZeroAlloc primitives — typed ids, value objects, `Result<T, Error>` and the generated validators — allocate nothing per call, which `tests/MyApp.UnitTests/AllocationTests.cs` asserts; a full request through ASP.NET and ZA.ORM costs ~29–32 KB (see the table). Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

Where Clean Architecture splits the codebase by *technical layer* (Domain / Application / Infrastructure / Api, four csprojs, dependency direction strictly inward), vertical slice splits by *use case*: one folder per feature, one file per slice, every slice owns its request + validator + handler + endpoint + entity. It references the same 12 ZeroAlloc libraries as `za-clean` (Mediator, Validation, Authorization, Mapping, ValueObjects, Inject, Resilience, Rest, Results, Serialisation, ORM, Telemetry); the wiring is inverted.

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
# → Healthy
```

The API boots, applies its ZA.ORM-managed embedded SQL migrations, and listens on the Kestrel default. OpenTelemetry traces stream to the console.

> Pipeline + primitive numbers measured in CI on Ubuntu 24.04 / AMD EPYC / .NET 10.0.8 via [`Benchmarks (manual)` run 26778623747](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/actions/runs/26778623747). NBomber-Postgres at 5,000-RPS open-model inject (2026-06-10 capacity-recipe run on i9-12900HK) sustains 4,312 RPS / 0 failures (mean 2.7 ms, p50 1.1 ms, p95 4.0 ms, p99 13.6 ms; raw report: [`docs/benchmarks/2026-06-10-template-capacity-za-vs.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-10-template-capacity-za-vs.md)).

> ⚠️ **Capacity-recipe local numbers, not capacity claims.** The NBomber
> numbers above are from a single-laptop [`docs/benchmarks/capacity-recipe.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/capacity-recipe.md)
> run (i9-12900HK, Postgres pinned cores 0–1, SUT pinned cores 2–5, NBomber
> unpinned on host). The 4,312 RPS plateau is NBomber's per-machine injector
> cap at a 5 k target — see [`docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-05-nbomber-ceiling-sweep.md)
> for the rate-sweep that locates the actual SUT ceiling. Single-machine
> measurement has known variance — for production capacity numbers, run the
> recipe on your target hardware (two-machine LAN variant is gold-standard).

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
            ├── CreateCustomer/CreateCustomer.cs  owns Customer entity + [Instrument]ed ICustomerWriter over its [Command] partial
            └── GetCustomer/GetCustomer.cs        [Query] partial co-located

tests/
├── MyApp.UnitTests/         xUnit — handler-level unit tests, one folder per slice
├── MyApp.ConventionTests/   NetArchTest — vertical-slice conventions enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — endpoint roundtrips

benchmarks/
├── MyApp.Benchmarks.Primitives/  BenchmarkDotNet — ZA primitives in isolation
├── MyApp.Benchmarks/             BenchmarkDotNet — full ASP.NET + ZA.ORM pipeline cost
└── MyApp.LoadTest/               NBomber — RPS under sustained concurrency
```

## The canonical slice — `Features/Orders/PlaceOrder/PlaceOrder.cs`

One file holds every concept that participates in `POST /orders`: the request with its validation attributes, the handler with its ZA.ORM `[Command]` partial, the endpoint, and the `Order` entity. This is the file as shipped, without its XML doc comments:

```csharp
using System.Data.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Orders.PlaceOrder;

[Validate]
[RequirePolicy("OrdersWrite")]
public readonly record struct PlaceOrderCommand(
    CustomerId CustomerId,
    [property: GreaterThan(0)] decimal Total)
    : IRequest<Result<OrderId, Error>>;

[Scoped]
public sealed partial class PlaceOrderHandler(IAsyncDbConnection conn)
    : IRequestHandler<PlaceOrderCommand, Result<OrderId, Error>>
{
    public async ValueTask<Result<OrderId, Error>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        // Validator (`[GreaterThan(0)]` on Total) guarantees cmd.Total > 0,
        // so Money.TryCreate cannot fail here. Money is internal-only — the
        // wire surface (PlaceOrderCommand.Total) stays bare decimal.
        var money = Money.TryCreate(cmd.Total, "EUR").Value;
        var id = await InsertOrderAsync(
            cmd.CustomerId.Value,
            MoneyConverter.ToStorage(money),
            nameof(OrderStatus.Pending),
            ct).ConfigureAwait(false);
        return Result<OrderId, Error>.Success(new OrderId(id));
    }

    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Total\", \"Status\") VALUES (@customerId, @total, @status) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertOrderAsync(int customerId, string total, string status, CancellationToken ct);
}

public static class PlaceOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", static async (PlaceOrderCommand cmd, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    await cache.EvictByTagAsync("orders", ct).ConfigureAwait(false);
                    return Results.Created($"/orders/{result.Value.Value}", result.Value);
                }

                return result.Error.ToProblem();
            })
            .RequireAuthorization("OrdersWrite");
}

public sealed class Order
{
    public Order(CustomerId customerId, decimal total)
    {
        Id = new OrderId(0);
        CustomerId = customerId;
        Total = total;
        Status = OrderStatus.Pending;
    }

    public OrderId Id { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public decimal Total { get; private set; }

    public OrderStatus Status { get; private set; }

    public bool Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        Status = OrderStatus.Cancelled;
        return true;
    }
}

public enum OrderStatus
{
    Pending = 0,
    Cancelled = 1,
}
```

`[Validate]` makes ZA.Validation generate `PlaceOrderCommandValidator`, which the ZA.Mediator.Validation behaviour runs before the handler. `[RequirePolicy("OrdersWrite")]` is checked by the mediator's authorization behaviour, on top of `RequireAuthorization("OrdersWrite")` on the endpoint.

To add a new use case — copy a slice file, rename, tweak, and add one `XxxEndpoint.Map(app)` line to `Program.cs` (explicit, so endpoint mapping stays reflection-free for NativeAOT). The generated `services.AddMediator()` registers the new handler at compile time, and the `[RequirePolicy]` declaration is enforced by the mediator's authorization behaviour automatically.

## Conventions enforced by `MyApp.ConventionTests`

- Every `*Command` / `*Query` under `MyApp.Features` implements `IRequest<>`.
- Every `*Handler` is `public sealed`.
- Every `*Handler` implements `IRequestHandler<,>`.
- Every endpoint is a `public static` class.

Cross-slice references are deliberately not a rule: entities such as `Order` are owned by one slice and read by others. A new cross-slice `using` is left to code review.

Violations fail CI.

## Output caching

`GET /orders/{id}` and `GET /customers/{id}` are wrapped in ASP.NET Core OutputCaching with per-entity configurable TTL (defaults: 30 seconds each). Concurrent same-id reads are served from the in-memory cache, absorbing pressure on the Npgsql connection pool under load — see [docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-06-08-189-dotnet-counters-vs.md) for the empirical investigation that motivated this layer.

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

## Telemetry

`Program.cs` sends traces and metrics through OpenTelemetry: the console exporter in `Development`, OTLP otherwise. Besides ASP.NET Core, HttpClient and ZA.Mediator spans, it subscribes to the app's own source and meter, named by `Telemetry.SourceName` in `Common/Telemetry.cs`.

The CreateCustomer slice shows ZA.Telemetry. Unlike the other slices, it keeps its ZA.ORM `[Command]` partial behind a slice-owned `ICustomerWriter` port. `[Instrument(Telemetry.SourceName)]` on that interface makes the source generator emit `CustomerWriterInstrumented`, and `AddMyApp` registers that proxy in front of `CustomerWriter`. Every insert records a `customer.insert` span, and every successful one adds 1 to the `customers.created` counter. The proxy is plain generated code, so it is Native AOT safe. `tests/MyApp.IntegrationTests/Features/Customers/CreateCustomer/CustomerWriterTelemetryTests.cs` asserts the registration and both signals. For hand-written spans and instruments, use `Telemetry.ActivitySource` and `Telemetry.Meter`.

## Extending

- **AI agents**: [AGENTS.md](AGENTS.md) — orientation for Claude Code, Cursor, GitHub Copilot, Codex, Aider. Includes "how to add a slice" recipes and the ZA-specific gotchas.
- **Swap SQLite → PostgreSQL**: set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your Postgres conn string. Startup runs ZA.ORM's `MigrationRunner` over the embedded SQL files under `src/MyApp/Persistence/Migrations/Postgres/` — no reflection, AOT-clean. After entity or schema changes, hand-author a new migration file under both providers:
  ```
  src/MyApp/Persistence/Migrations/Sqlite/002_add_customer_email.sql
  src/MyApp/Persistence/Migrations/Postgres/002_add_customer_email.sql
  ```
  File-naming convention: `NNN_description.sql` — a 3+ digit zero-padded version prefix (strictly increasing) plus a snake_case description. `MigrationRunner` orders by the prefix and applies anything newer than the recorded high-water mark on next startup.

## License

MIT — see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/LICENSE).
