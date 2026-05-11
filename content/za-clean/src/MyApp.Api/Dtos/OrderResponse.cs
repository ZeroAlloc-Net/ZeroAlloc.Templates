namespace MyApp.Api.Dtos;

public sealed record OrderResponse(
    int OrderId,
    int CustomerId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineResponse> Lines);
