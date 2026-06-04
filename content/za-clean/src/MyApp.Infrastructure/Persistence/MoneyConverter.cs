using System.Globalization;
using MyApp.Domain.ValueObjects;

namespace MyApp.Infrastructure.Persistence;

/// <summary>
/// Round-trip helpers for the <see cref="Money"/> value object's TEXT storage
/// format <c>"&lt;amount&gt;|&lt;currency&gt;"</c>. Used by the ZA.ORM-emitted
/// read/write paths in <see cref="OrderRepository"/>.
/// </summary>
public static class MoneyConverter
{
    public static string ToStorage(Money m)
        => m.Amount.ToString(CultureInfo.InvariantCulture) + "|" + m.Currency;

    /// <summary>
    /// Parses just the decimal amount from the "amount|currency" storage format,
    /// skipping the currency token entirely. Used by read paths that project to a
    /// flat DTO and never read the currency back (e.g. per-line totals on
    /// <see cref="MyApp.Application.GetOrderById.OrderReadModel"/>, where only
    /// the order-level currency is exposed). Zero allocations — operates on a
    /// span over the source string.
    /// </summary>
    public static decimal AmountFromStorage(string raw)
    {
        var pipe = raw.IndexOf('|');
        var amountSpan = pipe < 0 ? raw.AsSpan() : raw.AsSpan(0, pipe);
        return decimal.Parse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public static Money FromStorage(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var pipe = s.IndexOf('|');
        if (pipe < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var amountSpan = s.AsSpan(0, pipe);
        var currency = s[(pipe + 1)..];
        if (amountSpan.IsEmpty || string.IsNullOrEmpty(currency)
            || !decimal.TryParse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        return Money.TryCreate(amount, currency).Value;
    }
}
