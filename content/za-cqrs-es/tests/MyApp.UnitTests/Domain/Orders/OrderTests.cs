using System;
using MyApp.Domain.Orders;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Domain.Orders;

public class OrderTests
{
    [Fact]
    public void Place_raises_OrderPlaced_and_transitions_to_Placed_status()
    {
        using var order = new Order();
        order.SetId(new OrderId(Guid.NewGuid()));
        var customerId = new CustomerId(Guid.NewGuid());

        order.Place(customerId, 99.99m, "EUR");

        Assert.Equal(OrderStatus.Placed, order.State.Status);
        Assert.Equal(customerId, order.State.CustomerId);
        Assert.Equal(99.99m, order.State.Total);
        Assert.Equal("EUR", order.State.Currency);
        Assert.Equal(1UL, (ulong)order.Version.Value);
    }

    [Fact]
    public void Place_in_already_placed_state_throws()
    {
        using var order = new Order();
        order.SetId(new OrderId(Guid.NewGuid()));
        var customerId = new CustomerId(Guid.NewGuid());
        order.Place(customerId, 10m, "EUR");

        var ex = Assert.Throws<InvalidOperationException>(() => order.Place(customerId, 20m, "EUR"));
        Assert.Contains("Placed", ex.Message, StringComparison.Ordinal);
    }
}
