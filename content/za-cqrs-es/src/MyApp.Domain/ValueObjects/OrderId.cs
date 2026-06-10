using System;

namespace MyApp.Domain.ValueObjects;

/// <summary>
/// Order aggregate identifier. <see cref="Guid"/>-backed so per-aggregate
/// event-store stream IDs (<c>order-{guid}</c>) are globally unique across
/// nodes — a sequence-allocated identifier does not survive an event-sourced
/// distributed deployment.
/// </summary>
/// <remarks>
/// Declared as <c>readonly record struct</c> rather than
/// <c>[TypedId(Strategy = IdStrategy.Uuid7)]</c> because the typed-id generator
/// emits an <c>internal sealed</c> nested <c>TypedIdJsonConverter</c>, and
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> source
/// generation rejects converters that lack an accessible parameterless
/// constructor (SYSLIB1220 + SYSLIB1030). The record-struct form serializes
/// naturally through STJ source-gen as <c>{"Value":"guid"}</c> with no extra
/// wiring. Tracked as upstream gap "ZA.ValueObjects [TypedId] + STJ source-gen
/// integration".
/// </remarks>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}
