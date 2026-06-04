using MyApp.Domain.ValueObjects;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

[RequirePolicy("OrdersRead")]
public sealed record GetOrderByIdQuery(OrderId OrderId) : IRequest<Result<OrderReadModel, ApplicationError>>;
