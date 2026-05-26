# MyApp

Vertical Slice Architecture Web API. Source-generated, AOT-friendly, zero-allocation through the framework hot path. Built on the [ZeroAlloc.\*](https://github.com/ZeroAlloc-Net) ecosystem.

Where Clean Architecture splits the codebase by *technical layer* (Domain / Application / Infrastructure / Api, four csprojs, dependency direction strictly inward), vertical slice splits by *use case*: one folder per feature, one file per slice, every slice owns its request + validator + handler + endpoint + entity. The 10-package ZeroAlloc showcase is identical; the wiring is inverted.

| | Value |
|---|---:|
| **AOT binary size** | ~36 MB single-file, self-contained (win-x64) |
| **AOT cold start** | ~1.0 s (process → `/healthz` 200, best of 3) |
| **Framework primitives end-to-end** | ~165 ns / 200 B (= mapping cost alone; chain adds 0 B) |
| **Mediator dispatch alone** | ~37 ns / 0 B |

**Reproduce:**

```bash
# Framework primitives (zero-alloc, ns/op — what ZA is)
dotnet run -c Release --project benchmarks/MyApp.Benchmarks.Primitives -- --filter "*"

# Full pipeline (ASP.NET + EF — what the platform costs)
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

The API boots, applies its EF Core SQLite migrations, and listens on the Kestrel default. OpenTelemetry traces stream to the console.

## Layout

```
src/
└── MyApp/                                One assembly, everything inside it.
    ├── Program.cs                        DI wiring + endpoint-discovery walk
    ├── Common/                           Shared primitives: TypedIds, Errors, Telemetry
    ├── Authorization/Policies.cs         [Policy] declarations
    ├── Persistence/                      AppDbContext + Migrations
    └── Features/
        ├── Orders/
        │   ├── PlaceOrder/PlaceOrder.cs    request + validator + handler + endpoint + Order entity
        │   ├── GetOrder/GetOrder.cs        request + validator + handler + endpoint
        │   ├── ListOrders/ListOrders.cs    paged read
        │   └── CancelOrder/CancelOrder.cs  state transition
        └── Customers/
            ├── CreateCustomer/CreateCustomer.cs  owns Customer entity
            └── GetCustomer/GetCustomer.cs

tests/
├── MyApp.UnitTests/         xUnit — handler-level unit tests, one folder per slice
├── MyApp.ConventionTests/   NetArchTest — vertical-slice conventions enforced
└── MyApp.IntegrationTests/  WebApplicationFactory — endpoint roundtrips

benchmarks/
├── MyApp.Benchmarks.Primitives/  BenchmarkDotNet — ZA primitives in isolation (0 B framework cost)
├── MyApp.Benchmarks/             BenchmarkDotNet — full ASP.NET + EF pipeline cost
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

// Handler — owns the DB work.
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

// Persistence entity — owned by this slice. AppDbContext exposes the DbSet.
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

## Extending

- **AI agents**: [AGENTS.md](AGENTS.md) — orientation for Claude Code, Cursor, GitHub Copilot, Codex, Aider. Includes "how to add a slice" recipes and the ZA-specific gotchas.
- **Swap SQLite → PostgreSQL**: change `UseSqlite` to `UseNpgsql` in `Program.cs`, add the EF provider, regenerate migrations.

## License

MIT — see [LICENSE](LICENSE).
