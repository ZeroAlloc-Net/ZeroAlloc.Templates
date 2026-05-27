using MyApp.Application;
using MyApp.Application.CreateOrder;
using MyApp.Domain.ValueObjects;
using Xunit;
using ZeroAlloc.Results;

namespace MyApp.UnitTests.Application;

public class CreateOrderHandlerTests
{
    [Fact]
    public async Task Returns_success_with_order_persisted()
    {
        var repo = new FakeOrderRepository();
        var shipping = new FakeShippingClient(quoteAmount: 5m);
        var handler = new CreateOrderHandler(repo, shipping);

        var cmd = new CreateOrderCommand(
            CustomerId: new CustomerId(42),
            Items: new List<OrderItem> { new("SKU-1", 2, 15m) },
            ShippingZip: "1011AA");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.Saved);
        Assert.Equal(42, repo.Saved[0].CustomerId.Value);
        Assert.Single(repo.Saved[0].Lines);
    }

    [Fact]
    public async Task Returns_failure_when_shipping_unavailable()
    {
        var repo = new FakeOrderRepository();
        var shipping = new FakeShippingClient(error: "carrier offline");
        var handler = new CreateOrderHandler(repo, shipping);

        var cmd = new CreateOrderCommand(
            CustomerId: new CustomerId(42),
            Items: new List<OrderItem> { new("SKU-1", 1, 10m) },
            ShippingZip: "1011AA");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("shipping.unavailable", result.Error.Code);
        Assert.Empty(repo.Saved);
    }

    private sealed class FakeShippingClient : IShippingQuoteClient
    {
        private readonly decimal? _quoteAmount;
        private readonly string? _error;

        public FakeShippingClient(decimal quoteAmount)
        {
            _quoteAmount = quoteAmount;
        }

        public FakeShippingClient(string error)
        {
            _error = error;
        }

        public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        {
            if (_error is not null)
            {
                return Task.FromResult(Result<Money, string>.Failure(_error));
            }

            var money = Money.TryCreate(_quoteAmount!.Value, "EUR").Value;
            return Task.FromResult(Result<Money, string>.Success(money));
        }
    }
}
