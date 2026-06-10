using System.Threading;
using System.Threading.Tasks;
using MyApp.Domain.ValueObjects;

#pragma warning disable MA0048 // OrderListing record co-located with IOrderListingsRepository

namespace MyApp.Application;

/// <summary>
/// Read-side projection repository for the <c>order_listings</c> denormalized
/// table. Implemented by <c>OrderListingsRepository</c> in Infrastructure using
/// ZA.ORM <c>[Command]</c>/<c>[Query]</c> partials.
/// </summary>
public interface IOrderListingsRepository
{
    /// <summary>Upserts a single order_listings row materialized from an OrderPlaced event.</summary>
    Task UpsertAsync(OrderId orderId, CustomerId customerId, string status, decimal total, string currency, CancellationToken ct);

    /// <summary>Reads the listing row for the given order id; null if no matching projection exists yet.</summary>
    Task<OrderListing?> GetByIdAsync(OrderId orderId, CancellationToken ct);
}

/// <summary>Projection row materialized from Order events (denormalized read model).</summary>
public sealed record OrderListing(OrderId Id, CustomerId CustomerId, string Status, decimal Total, string Currency);
