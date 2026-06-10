using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Dtos;
using MyApp.Domain.ValueObjects;

namespace MyApp.Api;

[JsonSerializable(typeof(PlaceOrderRequest))]
[JsonSerializable(typeof(CreatedOrderResponse))]
[JsonSerializable(typeof(CustomerId))]
[JsonSerializable(typeof(OrderId))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class JsonContext : JsonSerializerContext { }
