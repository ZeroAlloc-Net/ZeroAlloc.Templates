namespace MyApp.Application.CreateOrder;

/// <summary>
/// First-failure validation error returned by <see cref="CreateOrderValidator"/>.
/// </summary>
public sealed record ValidationError(string Field, string Message);
