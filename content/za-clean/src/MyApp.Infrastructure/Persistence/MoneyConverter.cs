using System.Globalization;
using MyApp.Domain.ValueObjects;

namespace MyApp.Infrastructure.Persistence;

/// <summary>
/// Round-trip helpers for the <see cref="Money"/> value object's TEXT storage
/// format <c>"&lt;amount&gt;|&lt;currency&gt;"</c>. Used by both the EF Core
/// <c>ValueConverter</c> in <see cref="Configurations.OrderConfiguration"/> and
/// the raw-SQL read path in <see cref="OrderRepository"/>.
/// </summary>
/// <remarks>
/// Centralised here (rather than left as a private helper on
/// <c>OrderConfiguration</c>) because the raw-SQL materialisation in
/// <see cref="OrderRepository.GetByIdAsync"/> hand-hydrates Money columns and
/// must use the exact same parse rules the converter uses for storage.
/// </remarks>
public static class MoneyConverter
{
    public static string ToStorage(Money m)
        => m.Amount.ToString(CultureInfo.InvariantCulture) + "|" + m.Currency;

    public static Money FromStorage(string s)
    {
        // EF Core probes the converter with the property's sentinel value (the
        // default string, i.e. empty) during model initialisation to compute
        // a sentinel for the converted CLR type. Return a zero Money for that
        // case rather than throwing.
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
