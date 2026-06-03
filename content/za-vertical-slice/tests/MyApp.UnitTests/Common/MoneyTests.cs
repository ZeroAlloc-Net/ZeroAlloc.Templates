using MyApp.Common;
using Xunit;

namespace MyApp.UnitTests.Common;

public class MoneyTests
{
    [Fact]
    public void Money_rejects_negative_amount()
    {
        var result = Money.TryCreate(-1.00m, "EUR");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_rejects_empty_currency()
    {
        var result = Money.TryCreate(1.00m, "");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_accepts_valid_amount()
    {
        var result = Money.TryCreate(10.00m, "EUR");
        Assert.True(result.IsSuccess);
        Assert.Equal(10.00m, result.Value.Amount);
        Assert.Equal("EUR", result.Value.Currency);
    }

    [Fact]
    public void Money_accepts_zero_amount()
    {
        // za-clean's semantics: non-negative (>= 0). vs follows suit so the
        // two templates stay aligned. Strict-positive is enforced at the API
        // boundary via [GreaterThan(0)] on PlaceOrderCommand.Total.
        var result = Money.TryCreate(0m, "EUR");
        Assert.True(result.IsSuccess);
    }
}
