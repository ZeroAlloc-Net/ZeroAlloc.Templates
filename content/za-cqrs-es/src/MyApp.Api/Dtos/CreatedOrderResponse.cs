using MyApp.Domain.ValueObjects;

namespace MyApp.Api.Dtos;

/// <summary>Response returned by <c>POST /orders</c> on success.</summary>
public sealed record CreatedOrderResponse(OrderId Id);
