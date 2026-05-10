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
}
