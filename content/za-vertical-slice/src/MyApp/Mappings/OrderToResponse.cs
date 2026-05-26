using MyApp.Api.Dtos;
using MyApp.Domain;

namespace MyApp.Api.Mappings;

/// <summary>
/// Application/Domain → wire-format projector. Hand-written because the response
/// shape involves multiple ZA.ValueObjects unwraps (Id.Value, CustomerId.Value,
/// Total.Amount, Total.Currency) plus an enum-to-string projection for Status —
/// ZA.Mapping's generator doesn't currently emit enum→string, so the explicit
/// projector is clearer than fighting attribute knobs.
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
            OrderId: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            Status: order.Status.ToString(),
            Total: order.Total.Amount,
            Currency: order.Total.Currency,
            Lines: lines);
    }
}
