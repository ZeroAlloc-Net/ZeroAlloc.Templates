using MyApp.Domain;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

public sealed record GetOrderByIdQuery(int OrderId) : IRequest<Result<Order, ApplicationError>>;
