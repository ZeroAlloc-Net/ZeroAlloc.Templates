namespace MyApp.Application.CreateOrder;

public sealed record OrderItem(
    string Sku,
    int Quantity,
    decimal UnitPriceEur);
