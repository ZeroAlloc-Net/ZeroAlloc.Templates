using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Dtos;
using MyApp.Domain.ValueObjects;

namespace MyApp.Api;

// Request/response DTOs carry CustomerId/OrderId typed IDs. The
// ZA.ValueObjects-generated JSON converter serialises each as a bare integer,
// so the wire format is unchanged from when these properties were raw int.
// Registering the typed IDs explicitly here keeps the AOT type-info graph
// closed and tolerant of future endpoints returning a typed ID directly.
[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderResponse))]
[JsonSerializable(typeof(OrderItemDto))]
[JsonSerializable(typeof(OrderLineResponse))]
[JsonSerializable(typeof(CreatedOrderResponse))]
[JsonSerializable(typeof(CustomerId))]
[JsonSerializable(typeof(OrderId))]
[JsonSerializable(typeof(IReadOnlyList<OrderItemDto>))]
[JsonSerializable(typeof(IReadOnlyList<OrderLineResponse>))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class JsonContext : JsonSerializerContext { }
