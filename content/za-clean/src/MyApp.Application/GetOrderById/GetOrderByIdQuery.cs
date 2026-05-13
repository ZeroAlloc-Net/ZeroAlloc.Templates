using MyApp.Domain;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

[Authorize("OrdersRead")]
public sealed record GetOrderByIdQuery(int OrderId) : IRequest<Result<Order, ApplicationError>>;
