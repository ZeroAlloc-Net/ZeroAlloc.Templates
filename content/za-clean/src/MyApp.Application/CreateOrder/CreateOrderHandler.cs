using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Application.CreateOrder;

[Scoped]
public sealed class CreateOrderHandler(IOrderRepository repo, IShippingQuoteClient shipping)
    : IRequestHandler<CreateOrderCommand, Result<OrderId, ApplicationError>>
{
    public async ValueTask<Result<OrderId, ApplicationError>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var validation = CreateOrderValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result<OrderId, ApplicationError>.Failure(
                new ApplicationError("validation.failed", $"{validation.Error.Field}: {validation.Error.Message}"));
        }

        var quote = await shipping.GetQuoteAsync(request.ShippingZip, ct).ConfigureAwait(false);
        if (quote.IsFailure)
        {
            return Result<OrderId, ApplicationError>.Failure(new ApplicationError("shipping.unavailable", quote.Error));
        }

        var order = Order.Create(request.CustomerId);
        foreach (var item in request.Items)
        {
            var price = Money.TryCreate(item.UnitPriceEur, "EUR");
            if (price.IsFailure)
            {
                return Result<OrderId, ApplicationError>.Failure(new ApplicationError("price.invalid", price.Error));
            }

            order.AddLine(item.Sku, item.Quantity, price.Value);
        }

        var persisted = await repo.AddAsync(order, ct).ConfigureAwait(false);
        return Result<OrderId, ApplicationError>.Success(persisted.Id);
    }
}
