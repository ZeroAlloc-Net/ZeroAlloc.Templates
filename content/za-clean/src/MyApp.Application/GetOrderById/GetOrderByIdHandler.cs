using System.Globalization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.GetOrderById;

[Scoped]
public sealed class GetOrderByIdHandler(IOrderRepository repo)
    : IRequestHandler<GetOrderByIdQuery, Result<OrderReadModel, ApplicationError>>
{
    public async ValueTask<Result<OrderReadModel, ApplicationError>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await repo.GetByIdAsync(request.OrderId, ct).ConfigureAwait(false);
        return order is null
            ? Result<OrderReadModel, ApplicationError>.Failure(new ApplicationError(
                "order.not-found",
                "Order " + request.OrderId.Value.ToString(CultureInfo.InvariantCulture) + " not found"))
            : Result<OrderReadModel, ApplicationError>.Success(order);
    }
}
