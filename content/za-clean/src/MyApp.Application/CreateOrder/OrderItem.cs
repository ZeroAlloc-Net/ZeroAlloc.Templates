using ZeroAlloc.Validation;

namespace MyApp.Application.CreateOrder;

[Validate]
public sealed record OrderItem(
    [property: NotEmpty] string Sku,
    [property: GreaterThan(0)] int Quantity,
    [property: GreaterThanOrEqualTo(0.0)] decimal UnitPriceEur);
