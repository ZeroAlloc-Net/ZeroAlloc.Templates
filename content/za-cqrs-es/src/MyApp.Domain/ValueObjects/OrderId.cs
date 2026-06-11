using ZeroAlloc.ValueObjects;

namespace MyApp.Domain.ValueObjects;

/// <summary>
/// Order aggregate identifier. UUIDv7-backed so per-aggregate event-store
/// stream IDs (<c>order-{guid}</c>) are time-ordered and globally unique
/// across nodes — a sequence-allocated identifier does not survive an
/// event-sourced distributed deployment.
/// </summary>
/// <remarks>
/// The generator-emitted <c>TypedIdJsonConverter</c> is <c>public sealed</c>
/// from ZeroAlloc.ValueObjects 1.7.1 onward, but System.Text.Json's source
/// generator does not observe cross-generator <c>[JsonConverter]</c>
/// attributes — register the converter explicitly on
/// <c>JsonSerializerOptions.Converters</c> in the API composition root
/// (see <c>Program.cs</c>) and on the event-store serializer
/// (see <c>MyAppEventSerializer</c>).
/// </remarks>
[TypedId(Strategy = IdStrategy.Uuid7)]
public readonly partial record struct OrderId;
