using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.CreateOrder;

public sealed record CreateOrderCommand(
    int CustomerId,
    IReadOnlyList<OrderItem> Items,
    string ShippingZip)
    : IRequest<Result<OrderId, ApplicationError>>;
