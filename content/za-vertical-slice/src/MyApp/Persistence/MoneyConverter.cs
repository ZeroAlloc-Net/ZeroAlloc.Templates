using System.Globalization;
using MyApp.Common;

namespace MyApp.Persistence;

/// <summary>
/// Round-trip helpers for the <see cref="Money"/> value object's TEXT storage
/// format <c>"&lt;amount&gt;|&lt;currency&gt;"</c>. Used by the ZA.ORM-emitted
/// read/write paths in the Orders slices.
/// </summary>
public static class MoneyConverter
{
    public static string ToStorage(Money m)
        => m.Amount.ToString(CultureInfo.InvariantCulture) + "|" + m.Currency;

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
