using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyApp.Domain.Orders.Events;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.EventSourcing;

#pragma warning disable MA0048 // JsonContext partial co-located with the serializer

namespace MyApp.Infrastructure.EventStore;

/// <summary>
/// AOT-clean event serializer using <see cref="MyAppEventJsonContext"/> (the
/// source-generated <see cref="JsonSerializerContext"/>). Every event type in
/// <see cref="MyAppEventTypeRegistry"/> must be registered here as a
/// <c>[JsonSerializable]</c> entry; otherwise serialize/deserialize throws.
/// </summary>
/// <remarks>
/// The <see cref="OrderId"/> / <see cref="CustomerId"/> <c>[TypedId]</c>
/// converters are wired onto <see cref="JsonSerializerOptions.Converters"/>
/// and a dedicated context instance is constructed from those options.
/// STJ's source generator does not observe the <c>[JsonConverter]</c>
/// attribute the TypedId generator emits — without explicit registration,
/// the TypedId properties inside <see cref="OrderPlaced"/> would serialize
/// as <c>{}</c> via STJ's POCO fallback.
/// </remarks>
internal sealed class MyAppEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly MyAppEventJsonContext Context = new(Options);

    public ReadOnlyMemory<byte> Serialize<TEvent>(TEvent @event) where TEvent : notnull
    {
        return @event switch
        {
            OrderPlaced p => JsonSerializer.SerializeToUtf8Bytes(p, Context.OrderPlaced),
            _ => throw new NotSupportedException($"Unsupported event type {typeof(TEvent).FullName}"),
        };
    }

    public object Deserialize(ReadOnlyMemory<byte> payload, Type eventType)
    {
        if (eventType == typeof(OrderPlaced))
        {
            return JsonSerializer.Deserialize(payload.Span, Context.OrderPlaced)
                ?? throw new InvalidOperationException("Deserialized OrderPlaced was null");
        }
        throw new NotSupportedException($"Unsupported event type {eventType.FullName}");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new OrderId.TypedIdJsonConverter());
        opts.Converters.Add(new CustomerId.TypedIdJsonConverter());
        return opts;
    }
}

[JsonSerializable(typeof(OrderPlaced))]
internal sealed partial class MyAppEventJsonContext : JsonSerializerContext { }
