using MyApp.Application;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Application.Orders.PlaceOrder;

/// <summary>
/// Write-side command for placing a new order. Validated by ZA.Validation
/// (generator-emitted from <c>[Validate]</c>) and authorized by
/// ZA.Mediator.Authorization (<c>[RequirePolicy("OrdersWrite")]</c>). Returns
/// the assigned <see cref="OrderId"/> on success.
/// </summary>
[Validate]
[RequirePolicy("OrdersWrite")]
public sealed record PlaceOrderCommand(
    CustomerId CustomerId,
    [property: GreaterThan(0.0)] decimal Total,
    [property: NotEmpty] string Currency)
    : IRequest<Result<OrderId, ApplicationError>>;
