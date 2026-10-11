using MyApp.Application.GetOrderById;
using MyApp.Domain.ValueObjects;

namespace MyApp.Api.Dtos;

/// <summary>
/// Response body returned from <c>GET /orders/{id}</c>. <see cref="Lines"/> reuses the
/// query-side <see cref="OrderLineReadModel"/> records: they already have the wire shape
/// (sku, quantity, price), so the endpoint serialises them as-is instead of projecting
/// every line into a second, identical record.
/// </summary>
public sealed record OrderResponse(
    OrderId OrderId,
    CustomerId CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineReadModel> Lines);
