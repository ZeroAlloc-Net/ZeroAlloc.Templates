using System;

namespace MyApp.Domain.ValueObjects;

/// <summary>
/// Customer aggregate identifier. <see cref="Guid"/>-backed for the same reason
/// as <see cref="OrderId"/>. Declared as <c>readonly record struct</c> rather
/// than using <c>[ValueObject]</c> — see the OrderId docs for the rationale.
/// </summary>
public readonly record struct CustomerId(Guid Value);
