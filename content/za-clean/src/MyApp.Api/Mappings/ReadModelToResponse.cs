using MyApp.Api.Dtos;
using MyApp.Application.GetOrderById;

namespace MyApp.Api.Mappings;

/// <summary>
/// Wire-format projector for the Order query read model. Flat field copy +
/// per-line array projection. No Domain types referenced — the read model
/// already carries the wire-friendly shape (string status, decomposed
/// decimal Total + string Currency, plain decimal per line).
/// </summary>
public static class ReadModelToResponse
{
    public static OrderResponse Map(OrderReadModel rm)
    {
        var lines = new OrderLineResponse[rm.Lines.Count];
        for (var i = 0; i < rm.Lines.Count; i++)
        {
            var line = rm.Lines[i];
            lines[i] = new OrderLineResponse(line.Sku, line.Quantity, line.Price);
        }
        return new OrderResponse(
            OrderId: rm.OrderId,
            CustomerId: rm.CustomerId,
            Status: rm.Status,
            Total: rm.Total,
            Currency: rm.Currency,
            Lines: lines);
    }
}
