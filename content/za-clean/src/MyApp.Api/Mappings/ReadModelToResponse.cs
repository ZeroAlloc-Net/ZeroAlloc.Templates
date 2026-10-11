using MyApp.Api.Dtos;
using MyApp.Application.GetOrderById;

namespace MyApp.Api.Mappings;

/// <summary>
/// Wire-format projector for the Order query read model. A flat field copy:
/// no Domain types referenced, because the read model already carries the
/// wire-friendly shape (string status, decomposed decimal Total + string
/// Currency, plain decimal per line). The line records are passed through
/// unchanged, so a read allocates each line once, in the repository.
/// </summary>
public static class ReadModelToResponse
{
    public static OrderResponse Map(OrderReadModel rm)
        => new(
            OrderId: rm.OrderId,
            CustomerId: rm.CustomerId,
            Status: rm.Status,
            Total: rm.Total,
            Currency: rm.Currency,
            Lines: rm.Lines);
}
