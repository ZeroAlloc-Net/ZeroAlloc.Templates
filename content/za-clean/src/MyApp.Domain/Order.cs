using MyApp.Domain.ValueObjects;

namespace MyApp.Domain;

public sealed class Order
{
    private readonly List<OrderLine> _lines = new();

    private Order(OrderId id, CustomerId customerId)
    {
        Id = id;
        CustomerId = customerId;
        Status = OrderStatus.Pending;
        Total = Money.TryCreate(0m, "EUR").Value;
    }

    // EF Core materialisation constructor. The framework rehydrates [CustomerId]
    // and [Total] through the configured property/owned-type mappings; the field
    // initialisers above keep [_lines] non-null and EF assigns OrderStatus via
    // its value-converter.
    private Order()
    {
    }

    // Setter is private because EF assigns Id on INSERT via its value-converter;
    // domain code must not mutate it after creation.
    public OrderId Id { get; private set; }

    public CustomerId CustomerId { get; }

    public OrderStatus Status { get; private set; }

    public Money Total { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Create(CustomerId customerId)
        => new(new OrderId(0), customerId);

    public void AddLine(string sku, int quantity, Money price)
    {
        _lines.Add(new OrderLine(sku, quantity, price));
        Total = Money.TryCreate(Total.Amount + (price.Amount * quantity), Total.Currency).Value;
    }

    /// <summary>
    /// Re-hydration factory for infrastructure code that materialises an Order
    /// from raw storage (e.g. the raw-SQL read path in
    /// <c>OrderRepository.GetByIdAsync</c>). Bypasses domain invariants
    /// (totals are not recomputed from lines) because the caller is replaying
    /// already-validated persisted state, not running new business logic.
    /// Keep this out of the application layer's command/query handlers — use
    /// <see cref="Create"/> + <see cref="AddLine"/> for new orders.
    /// </summary>
    public static Order Materialize(
        OrderId id,
        CustomerId customerId,
        OrderStatus status,
        Money total,
        IEnumerable<OrderLine> lines)
    {
        var order = new Order(id, customerId)
        {
            Status = status,
            Total = total,
        };
        foreach (var line in lines)
        {
            order._lines.Add(line);
        }
        return order;
    }
}
