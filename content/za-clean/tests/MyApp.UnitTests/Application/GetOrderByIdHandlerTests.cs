using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Application;

public class GetOrderByIdHandlerTests
{
    [Fact]
    public async Task Returns_failure_when_order_not_found()
    {
        var repo = new FakeOrderRepository();
        var handler = new GetOrderByIdHandler(repo);

        var result = await handler.Handle(new GetOrderByIdQuery(OrderId: new OrderId(999)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("order.not-found", result.Error.Code);
    }

    [Fact]
    public async Task Returns_read_model_with_flat_fields_when_found()
    {
        var repo = new FakeOrderRepository();
        var order = Order.Create(new CustomerId(7));
        order.AddLine("SKU-A", 2, Money.TryCreate(15m, "EUR").Value);
        var saved = await repo.AddAsync(order, CancellationToken.None);

        var handler = new GetOrderByIdHandler(repo);
        var result = await handler.Handle(new GetOrderByIdQuery(saved.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rm = result.Value;
        Assert.Equal(saved.Id, rm.OrderId);
        Assert.Equal(new CustomerId(7), rm.CustomerId);
        Assert.Equal("Pending", rm.Status);
        Assert.Equal("EUR", rm.Currency);
        Assert.Single(rm.Lines);
        Assert.Equal("SKU-A", rm.Lines[0].Sku);
        Assert.Equal(2, rm.Lines[0].Quantity);
        Assert.Equal(15m, rm.Lines[0].Price);
    }
}
