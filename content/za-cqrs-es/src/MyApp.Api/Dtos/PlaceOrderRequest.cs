using System;

namespace MyApp.Api.Dtos;

/// <summary>
/// HTTP-shaped request for <c>POST /orders</c>. Carries primitive types so the
/// AOT JSON serializer doesn't need to know about typed-ID converters at the
/// wire boundary; the endpoint wraps them into domain value objects before
/// dispatching the command.
/// </summary>
public sealed record PlaceOrderRequest(Guid CustomerId, decimal Total, string Currency);
