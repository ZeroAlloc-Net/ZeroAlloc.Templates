namespace MyApp.Api.Dtos;

/// <summary>
/// Wire-format line item. Named distinctly from <c>MyApp.Application.CreateOrder.OrderItem</c>
/// to avoid namespace ambiguity; ZA.Mapping bridges the two with a generated <c>Map</c>.
/// </summary>
public sealed record OrderItemDto(string Sku, int Quantity, decimal UnitPriceEur);
