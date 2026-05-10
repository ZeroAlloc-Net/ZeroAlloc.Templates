using ZeroAlloc.ValueObjects;

namespace MyApp.Domain.ValueObjects;

[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }

    public CustomerId(int value)
    {
        Value = value;
    }
}
