using MyApp.Api.Dtos;
using MyApp.Application.CreateOrder;
using ZeroAlloc.Mapping;

namespace MyApp.Api.Mappings;

/// <summary>
/// Wire-format → Application command at the HTTP boundary.
/// Both <see cref="MapAttribute{TSource, TDestination}"/> entries live on the same partial class
/// so the generator's nested resolver can find the element-level <c>OrderItemDto → OrderItem</c>
/// mapping when emitting the <c>IReadOnlyList&lt;OrderItemDto&gt;</c> collection overload used by
/// the parent <c>OrderRequest → CreateOrderCommand</c> map.
/// </summary>
[Map<OrderRequest, CreateOrderCommand>]
[Map<OrderItemDto, OrderItem>]
public static partial class OrderRequestToCommand
{
}
