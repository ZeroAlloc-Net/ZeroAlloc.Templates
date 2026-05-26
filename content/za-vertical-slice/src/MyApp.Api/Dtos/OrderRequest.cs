namespace MyApp.Api.Dtos;

public sealed record OrderRequest(
    int CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    string ShippingZip);
