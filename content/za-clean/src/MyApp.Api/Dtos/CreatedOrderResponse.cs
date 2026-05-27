using MyApp.Domain.ValueObjects;

namespace MyApp.Api.Dtos;

/// <summary>
/// Response body returned from <c>POST /orders</c>. Concrete (non-anonymous)
/// so the source-generated <see cref="MyApp.Api.JsonContext"/> can serialise
/// it under NativeAOT. The <see cref="OrderId"/> typed-ID serialises as a
/// bare integer via the ZA.ValueObjects-generated JSON converter, so the
/// wire format is unchanged from the prior <c>int</c> shape.
/// </summary>
public sealed record CreatedOrderResponse(OrderId Id);
