using MyApp.Application;
using MyApp.Application.CreateOrder;
using MyApp.Domain.ValueObjects;
using Xunit;
using ZeroAlloc.Results;

namespace MyApp.UnitTests.Application;

public class CreateOrderHandlerValidationTests
{
    [Fact]
    public async Task Returns_validation_failure_for_bad_shipping_zip()
    {
        var repo = new FakeOrderRepository();
        var shipping = new FakeShippingClient(5m);
        var handler = new CreateOrderHandler(repo, shipping);
        var cmd = new CreateOrderCommand(new CustomerId(42), [new("SKU-1", 2, 15m)], "BAD-ZIP");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
        Assert.StartsWith("Shipping", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_validation_failure_when_items_empty()
    {
        var repo = new FakeOrderRepository();
        var shipping = new FakeShippingClient(5m);
        var handler = new CreateOrderHandler(repo, shipping);
        var cmd = new CreateOrderCommand(new CustomerId(42), [], "1011AA");

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
    }

    private sealed class FakeShippingClient : IShippingQuoteClient
    {
        private readonly decimal _quoteAmount;
        public FakeShippingClient(decimal quoteAmount) => _quoteAmount = quoteAmount;

        public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        {
            var money = Money.TryCreate(_quoteAmount, "EUR").Value;
            return Task.FromResult(Result<Money, string>.Success(money));
        }
    }
}
