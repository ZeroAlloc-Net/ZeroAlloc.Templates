namespace MyApp.Api.Dtos;

/// <summary>
/// Response body returned from <c>POST /orders</c>. Concrete (non-anonymous)
/// so the source-generated <see cref="MyApp.Api.JsonContext"/> can serialise
/// it under NativeAOT.
/// </summary>
public sealed record CreatedOrderResponse(int Id);
