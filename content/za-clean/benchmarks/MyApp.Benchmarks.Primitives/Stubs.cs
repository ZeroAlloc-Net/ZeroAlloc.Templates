using MyApp.Application;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

#pragma warning disable MA0048 // bench-only stubs co-located in one file

namespace MyApp.Benchmarks.Primitives;

/// <summary>
/// Zero-work <see cref="IOrderRepository"/> so <c>CreateOrderHandler</c> runs without a
/// database. The completed task is cached, so the stub itself allocates nothing per call.
/// </summary>
internal sealed class StubOrderRepository : IOrderRepository
{
    private static readonly Task<OrderId> Saved = Task.FromResult(new OrderId(1));
    private static readonly Task<int> Count = Task.FromResult(1);

    public Task<OrderId> AddAsync(Order order, CancellationToken ct) => Saved;

    public Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
        => throw new NotSupportedException("The primitives bench only dispatches CreateOrderCommand.");

    public Task<int> CountAsync(CancellationToken ct) => Count;
}

/// <summary>
/// Zero-work <see cref="IShippingQuoteClient"/> returning a cached successful quote.
/// </summary>
internal sealed class StubShippingQuoteClient : IShippingQuoteClient
{
    private static readonly Task<Result<Money, string>> Quote =
        Task.FromResult(Money.TryCreate(4.95m, "EUR"));

    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct) => Quote;
}

/// <summary>
/// Security context carrying the <c>orders.write</c> scope, so the
/// <c>[RequirePolicy("OrdersWrite")]</c> behaviour runs its policy and allows.
/// </summary>
internal sealed class StubSecurityContextAccessor : ISecurityContextAccessor
{
    public ISecurityContext Current { get; } = new StubContext();

    private sealed class StubContext : ISecurityContext
    {
        public string Id => "bench-user";
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string> Claims { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "scope", "orders.read orders.write" },
            };
    }
}
