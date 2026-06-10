using System;
using System.Collections.Generic;
using MyApp.Domain.Orders.Events;
using ZeroAlloc.EventSourcing;

namespace MyApp.Infrastructure.EventStore;

/// <summary>
/// Hand-rolled type-name &harr; CLR-type registry for the event store
/// serializer. Hand-rolled to keep the AOT trim graph closed — every event
/// type the store must round-trip is listed explicitly in <see cref="s_byName"/>.
/// Add a new event in three places: 1) the event record, 2) <see cref="s_byName"/>,
/// 3) the aggregate's <c>ApplyEvent</c> switch.
/// </summary>
internal sealed class MyAppEventTypeRegistry : IEventTypeRegistry
{
    private static readonly Dictionary<string, Type> s_byName = new(StringComparer.Ordinal)
    {
        [nameof(OrderPlaced)] = typeof(OrderPlaced),
    };

    public bool TryGetType(string eventType, out Type? type)
        => s_byName.TryGetValue(eventType, out type);

    public string GetTypeName(Type type) => type.Name;
}
