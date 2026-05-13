using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Application.CreateOrder;

[Validate]
public sealed record CreateOrderCommand(
    [property: GreaterThan(0)] int CustomerId,
    [property: NotEmpty] IReadOnlyList<OrderItem> Items,
    [property: NotEmpty, Matches("^[0-9]{4}[A-Z]{2}$")] string ShippingZip)
    : IRequest<Result<OrderId, ApplicationError>>;
