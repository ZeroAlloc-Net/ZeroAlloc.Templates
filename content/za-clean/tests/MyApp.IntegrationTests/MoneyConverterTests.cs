using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure.Persistence;
using Xunit;

// Pure unit-shape tests (no DB/IO) for a converter that happens to live in
// the Infrastructure layer. Sits in IntegrationTests because that's the test
// project that references Infrastructure transitively (via Api); MyApp.UnitTests
// is strictly layered to Domain + Application only.
namespace MyApp.IntegrationTests;

public class MoneyConverterTests
{
    [Fact]
    public void RoundTrip_preserves_amount_and_currency()
    {
        var original = Money.TryCreate(99.99m, "EUR").Value;
        var stored = MoneyConverter.ToStorage(original);
        var restored = MoneyConverter.FromStorage(stored);

        Assert.Equal(original.Amount, restored.Amount);
        Assert.Equal(original.Currency, restored.Currency);
    }

    [Fact]
    public void RoundTrip_preserves_fractional_pennies()
    {
        // Four-decimal precision survives string roundtrip (no IEEE float lossiness).
        var original = Money.TryCreate(0.0001m, "USD").Value;
        var stored = MoneyConverter.ToStorage(original);
        var restored = MoneyConverter.FromStorage(stored);

        Assert.Equal(0.0001m, restored.Amount);
    }

    [Fact]
    public void ToStorage_uses_invariant_culture()
    {
        // Stored form must be culture-independent (no locale-dependent decimal
        // separators) so production data is portable across machine cultures.
        var m = Money.TryCreate(1234.56m, "EUR").Value;
        var stored = MoneyConverter.ToStorage(m);
        Assert.Equal("1234.56|EUR", stored);
    }

    [Fact]
    public void FromStorage_empty_returns_zero_default()
    {
        var restored = MoneyConverter.FromStorage("");
        Assert.Equal(0m, restored.Amount);
        Assert.Equal("USD", restored.Currency);
    }

    [Fact]
    public void FromStorage_malformed_returns_zero_default()
    {
        // No pipe separator — defensive fallback per the converter's contract.
        var restored = MoneyConverter.FromStorage("not-a-money-value");
        Assert.Equal(0m, restored.Amount);
        Assert.Equal("USD", restored.Currency);
    }
}
