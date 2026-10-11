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

    /// <summary>
    /// Parses just the decimal amount from the "amount|currency" storage format,
    /// skipping the currency token entirely. Used by read slices that expose the
    /// amount only (GetOrder, ListOrders). Zero allocations — operates on a span
    /// over the source string.
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
        var currencySpan = s.AsSpan(pipe + 1);
        if (amountSpan.IsEmpty || currencySpan.IsEmpty
            || !decimal.TryParse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        return Money.TryCreate(amount, CurrencyString(currencySpan)).Value;
    }

    /// <summary>
    /// Returns the interned literal for the currency codes this app writes, so
    /// reading a stored value back does not allocate a new currency string per
    /// read. Any other code is materialised once per call.
    /// </summary>
    private static string CurrencyString(ReadOnlySpan<char> code) => code switch
    {
        "EUR" => "EUR",
        "USD" => "USD",
        _ => code.ToString(),
    };
}
