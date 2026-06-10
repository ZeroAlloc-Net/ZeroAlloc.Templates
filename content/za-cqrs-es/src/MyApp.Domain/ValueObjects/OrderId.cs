using System;

namespace MyApp.Domain.ValueObjects;

/// <summary>
/// Order aggregate identifier. <see cref="Guid"/>-backed because the event store
/// uses globally-unique stream IDs ("order-{guid}") that must not collide across
/// nodes — sequence-allocated identifiers do not survive an event-sourced
/// distributed deployment.
/// </summary>
/// <remarks>
/// Declared as <c>readonly record struct</c> instead of using
/// <c>[ValueObject]</c> from ZA.ValueObjects: the generator emits
/// <c>Value.ToString(CultureInfo.InvariantCulture)</c>, which doesn't compile
/// for <see cref="Guid"/> (no <c>ToString(IFormatProvider)</c> overload). The
/// record-struct gives equality + hash + ToString for free and is just as
/// allocation-free.
/// </remarks>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}
