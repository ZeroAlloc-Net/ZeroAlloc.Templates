using MyApp.Domain.ValueObjects;

namespace MyApp.Application.GetOrderById;

/// <summary>
/// Query-side projection of an Order, owned by Application. Read endpoints
/// populate this directly from storage — skipping the domain aggregate's
/// Money / OrderStatus reconstruction and the second Api-DTO array.
/// Writes still go through the <see cref="MyApp.Domain.Order"/> aggregate.
/// </summary>
public sealed record OrderReadModel(
    OrderId OrderId,
    CustomerId CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineReadModel> Lines);

public sealed record OrderLineReadModel(string Sku, int Quantity, decimal Price);
