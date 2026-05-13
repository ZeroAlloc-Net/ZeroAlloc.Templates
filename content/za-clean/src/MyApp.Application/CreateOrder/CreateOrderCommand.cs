using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Application.CreateOrder;

// `Items` has no [NotEmpty] attribute because [NotEmpty] in ZA.Validation 1.2.0
// emits a `string.IsNullOrEmpty(...)` check — string-only. The handler
// performs the "at least one item" check explicitly and returns a typed
// ApplicationError on the empty-collection case.
[Validate]
public sealed record CreateOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    IReadOnlyList<OrderItem> Items,
    [property: NotEmpty, Matches("^[0-9]{4}[A-Z]{2}$")] string ShippingZip)
    : IRequest<Result<OrderId, ApplicationError>>;
