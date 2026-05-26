using ZeroAlloc.ValueObjects;

namespace MyApp.Domain.ValueObjects;

[ValueObject]
public readonly partial struct OrderId
{
    public int Value { get; }

    public OrderId(int value)
    {
        Value = value;
    }
}
