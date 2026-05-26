using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Features.Customers.GetCustomer;
using MyApp.Features.Orders.GetOrder;
using MyApp.Features.Orders.ListOrders;
using MyApp.Features.Orders.PlaceOrder;

namespace MyApp;

/// <summary>
/// AOT-friendly source-generated JSON metadata for every type that crosses the
/// HTTP boundary. <c>PublishAot=true</c> removes the reflection-based default
/// type resolver at runtime; without an explicit source-gen context inserted
/// at index 0 of <c>JsonSerializerOptions.TypeInfoResolverChain</c>, ASP.NET
/// fails to start with
/// <c>NotSupportedException: JsonTypeInfo metadata for type '...' was not
/// provided by TypeInfoResolver of type '[]'</c>.
/// <para>
/// When you add a new slice, register every type that appears in a request
/// body, response body, or route value here. Forgetting to do so surfaces at
/// boot, not at compile time — the real-run-smoke job is the safety net.
/// </para>
/// </summary>
// Request bodies
[JsonSerializable(typeof(PlaceOrderCommand))]
[JsonSerializable(typeof(CreateCustomerCommand))]
// Typed-id wrappers returned from POST endpoints
[JsonSerializable(typeof(OrderId))]
[JsonSerializable(typeof(CustomerId))]
// Response DTOs
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(OrderPage))]
[JsonSerializable(typeof(OrderListItem))]
[JsonSerializable(typeof(CustomerDto))]
// Error shapes
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class JsonContext : JsonSerializerContext
{
}
