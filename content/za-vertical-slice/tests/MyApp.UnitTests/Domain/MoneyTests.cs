using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Money_rejects_negative_amount()
    {
        var result = Money.TryCreate(-1.00m, "EUR");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Money_accepts_valid_amount()
    {
        var result = Money.TryCreate(10.00m, "EUR");
        Assert.True(result.IsSuccess);
        Assert.Equal(10.00m, result.Value.Amount);
    }
}
