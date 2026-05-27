using ZeroAlloc.Results;
using ZeroAlloc.ValueObjects;

#pragma warning disable MA0048 // multiple value-objects intentionally co-located in one file (vertical-slice convention)

namespace MyApp.Common;

/// <summary>
/// Strongly-typed customer identifier. Generated equality / GetHashCode / ToString come
/// from <c>ZeroAlloc.ValueObjects</c>'s <c>[ValueObject]</c> source generator — no boxing
/// when used as a dictionary key or set member.
/// </summary>
[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }

    public CustomerId(int value)
    {
        Value = value;
    }
}

/// <summary>
/// Strongly-typed order identifier. Same generator-driven shape as <see cref="CustomerId"/>.
/// </summary>
[ValueObject]
public readonly partial struct OrderId
{
    public int Value { get; }

    public OrderId(int value)
    {
        Value = value;
    }
}

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
