using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Dtos;

namespace MyApp.Api;

[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderResponse))]
[JsonSerializable(typeof(OrderItemDto))]
[JsonSerializable(typeof(OrderLineResponse))]
[JsonSerializable(typeof(CreatedOrderResponse))]
[JsonSerializable(typeof(IReadOnlyList<OrderItemDto>))]
[JsonSerializable(typeof(IReadOnlyList<OrderLineResponse>))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class JsonContext : JsonSerializerContext { }
