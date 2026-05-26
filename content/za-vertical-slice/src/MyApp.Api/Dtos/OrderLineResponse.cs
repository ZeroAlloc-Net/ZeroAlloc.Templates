namespace MyApp.Api.Dtos;

public sealed record OrderLineResponse(string Sku, int Quantity, decimal Price);
