using MyApp.Domain.ValueObjects;

namespace MyApp.Api.Dtos;

public sealed record OrderRequest(
    CustomerId CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    string ShippingZip);
