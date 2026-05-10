using System.Globalization;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

[Scoped]
public sealed class GetOrderByIdHandler(IOrderRepository repo)
    : IRequestHandler<GetOrderByIdQuery, Result<Order, ApplicationError>>
{
    public async ValueTask<Result<Order, ApplicationError>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await repo.GetByIdAsync(new OrderId(request.OrderId), ct).ConfigureAwait(false);
        return order is null
            ? Result<Order, ApplicationError>.Failure(new ApplicationError(
                "order.not-found",
                "Order " + request.OrderId.ToString(CultureInfo.InvariantCulture) + " not found"))
            : Result<Order, ApplicationError>.Success(order);
    }
}
