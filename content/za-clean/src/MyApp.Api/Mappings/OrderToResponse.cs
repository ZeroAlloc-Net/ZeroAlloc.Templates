using MyApp.Api.Dtos;
using MyApp.Domain;

namespace MyApp.Api.Mappings;

/// <summary>
/// Application/Domain → wire-format projector. Now hand-projects only the
/// Money decomposition (Total.Amount, Total.Currency) and the enum-to-string
/// status — the typed IDs flow straight through unchanged because
/// <see cref="OrderResponse"/> carries them as typed properties and serialises
/// via the ZA.ValueObjects-generated JSON converter to the same bare-integer
/// wire format the prior <c>int</c>-shaped DTO produced.
/// </summary>
public static class OrderToResponse
{
    public static OrderResponse Map(Order order)
    {
        var lines = new OrderLineResponse[order.Lines.Count];
        for (var i = 0; i < order.Lines.Count; i++)
        {
            var line = order.Lines[i];
            lines[i] = new OrderLineResponse(line.Sku, line.Quantity, line.Price.Amount);
        }

        return new OrderResponse(
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            Status: order.Status.ToString(),
            Total: order.Total.Amount,
            Currency: order.Total.Currency,
            Lines: lines);
    }
}
