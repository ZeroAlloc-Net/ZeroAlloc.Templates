using MyApp.Domain.ValueObjects;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Application.CreateOrder;

// [Authorize] is checked by the ZA.Mediator.Authorization behavior — defense
// in depth: even if a future endpoint forgets to call RequireAuthorization,
// the handler refuses to run without the "orders.write" scope claim.
[Validate]
[Authorize("OrdersWrite")]
public sealed record CreateOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: NotEmpty] IReadOnlyList<OrderItem> Items,
    [property: NotEmpty, Matches("^[0-9]{4}[A-Z]{2}$")] string ShippingZip)
    : IRequest<Result<OrderId, ApplicationError>>;
