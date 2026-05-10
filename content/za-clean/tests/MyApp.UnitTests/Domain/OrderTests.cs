using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Domain;

public class OrderTests
{
    [Fact]
    public void Create_assigns_pending_status_and_zero_total()
    {
        var customerId = new CustomerId(42);
        var order = Order.Create(customerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(0m, order.Total.Amount);
        Assert.Equal("EUR", order.Total.Currency);
        Assert.Empty(order.Lines);
    }

    [Fact]
    public void AddLine_increments_total()
    {
        var order = Order.Create(new CustomerId(42));
        var price = Money.TryCreate(15m, "EUR").Value;
        order.AddLine("SKU-1", quantity: 2, price);
        Assert.Single(order.Lines);
        Assert.Equal(30m, order.Total.Amount);
    }
}
