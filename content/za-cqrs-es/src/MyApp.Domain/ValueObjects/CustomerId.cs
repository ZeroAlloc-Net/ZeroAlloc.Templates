using System;

namespace MyApp.Domain.ValueObjects;

/// <summary>Customer aggregate identifier — see <see cref="OrderId"/> for the Guid + STJ rationale.</summary>
public readonly record struct CustomerId(Guid Value);
