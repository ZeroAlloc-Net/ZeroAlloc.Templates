using MyApp.Domain.ValueObjects;

namespace MyApp.Domain;

public sealed record OrderLine(string Sku, int Quantity, Money Price);
