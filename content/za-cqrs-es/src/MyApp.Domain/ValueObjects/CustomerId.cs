using ZeroAlloc.ValueObjects;

namespace MyApp.Domain.ValueObjects;

/// <summary>Customer aggregate identifier — see <see cref="OrderId"/> for the UUIDv7 + STJ rationale.</summary>
[TypedId(Strategy = IdStrategy.Uuid7)]
public readonly partial record struct CustomerId;
