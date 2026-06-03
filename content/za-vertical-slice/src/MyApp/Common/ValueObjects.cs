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

