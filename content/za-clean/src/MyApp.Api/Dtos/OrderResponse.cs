using MyApp.Domain.ValueObjects;

namespace MyApp.Api.Dtos;

public sealed record OrderResponse(
    OrderId OrderId,
    CustomerId CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineResponse> Lines);
