using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyApp.Domain.Orders.Events;
using ZeroAlloc.EventSourcing;

#pragma warning disable MA0048 // JsonContext partial co-located with the serializer

namespace MyApp.Infrastructure.EventStore;

/// <summary>
/// AOT-clean event serializer using <see cref="MyAppEventJsonContext"/> (the
/// source-generated <see cref="JsonSerializerContext"/>). Every event type in
/// <see cref="MyAppEventTypeRegistry"/> must be registered here as a
/// <c>[JsonSerializable]</c> entry; otherwise serialize/deserialize throws.
/// </summary>
internal sealed class MyAppEventSerializer : IEventSerializer
{
    public ReadOnlyMemory<byte> Serialize<TEvent>(TEvent @event) where TEvent : notnull
    {
        return @event switch
        {
            OrderPlaced p => JsonSerializer.SerializeToUtf8Bytes(p, MyAppEventJsonContext.Default.OrderPlaced),
            _ => throw new NotSupportedException($"Unsupported event type {typeof(TEvent).FullName}"),
        };
    }

    public object Deserialize(ReadOnlyMemory<byte> payload, Type eventType)
    {
        if (eventType == typeof(OrderPlaced))
        {
            return JsonSerializer.Deserialize(payload.Span, MyAppEventJsonContext.Default.OrderPlaced)
                ?? throw new InvalidOperationException("Deserialized OrderPlaced was null");
        }
        throw new NotSupportedException($"Unsupported event type {eventType.FullName}");
    }
}

[JsonSerializable(typeof(OrderPlaced))]
internal sealed partial class MyAppEventJsonContext : JsonSerializerContext { }
