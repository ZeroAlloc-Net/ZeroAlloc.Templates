using ZeroAlloc.Validation;

namespace MyApp.Application.CreateOrder;

[Validate]
public readonly record struct OrderItem(
    [property: NotEmpty] string Sku,
    [property: GreaterThan(0)] int Quantity,
    [property: GreaterThanOrEqualTo(0)] decimal UnitPriceEur);
