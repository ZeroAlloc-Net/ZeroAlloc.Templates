using ZeroAlloc.Results;
using ZeroAlloc.ValueObjects;

namespace MyApp.Common;

/// <summary>
/// Non-negative monetary amount paired with an ISO-4217 currency code. Constructed via
/// <see cref="TryCreate"/> so invalid inputs surface as a <see cref="Result{T, TError}"/>
/// failure rather than throwing.
/// </summary>
[ValueObject]
public readonly partial struct Money
{
    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money, string> TryCreate(decimal amount, string currency)
    {
        if (amount < 0)
        {
            return Result<Money, string>.Failure("Amount must be non-negative");
        }

        if (string.IsNullOrEmpty(currency))
        {
            return Result<Money, string>.Failure("Currency required");
        }

        return Result<Money, string>.Success(new Money(amount, currency));
    }
}
