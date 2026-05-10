using MyApp.Application.GetOrderById;
using Xunit;

namespace MyApp.UnitTests.Application;

public class GetOrderByIdHandlerTests
{
    [Fact]
    public async Task Returns_failure_when_order_not_found()
    {
        var repo = new FakeOrderRepository();
        var handler = new GetOrderByIdHandler(repo);

        var result = await handler.Handle(new GetOrderByIdQuery(OrderId: 999), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("order.not-found", result.Error.Code);
    }
}
