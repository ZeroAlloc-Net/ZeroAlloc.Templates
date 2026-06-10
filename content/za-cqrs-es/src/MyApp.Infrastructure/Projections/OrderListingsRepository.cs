using System.Threading;
using System.Threading.Tasks;
using System.Data.Async;
using MyApp.Application;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.ORM;

namespace MyApp.Infrastructure.Projections;

/// <summary>
/// ZA.ORM-backed implementation of <see cref="IOrderListingsRepository"/>. The
/// <c>UpsertAsync</c> path uses a single INSERT … ON CONFLICT statement so
/// re-delivery of the same OrderPlaced event is idempotent — the projection
/// pipeline is at-least-once.
/// </summary>
[Scoped]
public sealed partial class OrderListingsRepository(IAsyncDbConnection conn) : IOrderListingsRepository
{
    public async Task UpsertAsync(OrderId orderId, CustomerId customerId, string status, decimal total, string currency, CancellationToken ct)
    {
        await UpsertCoreAsync(
            orderId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            customerId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            status,
            total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            currency,
            ct).ConfigureAwait(false);
    }

    public async Task<OrderListing?> GetByIdAsync(OrderId orderId, CancellationToken ct)
    {
        var r = await ReadByIdAsync(
            orderId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            ct).ConfigureAwait(false);
        if (r is null) return null;
        return new OrderListing(
            new OrderId(System.Guid.Parse(r.Id, System.Globalization.CultureInfo.InvariantCulture)),
            new CustomerId(System.Guid.Parse(r.CustomerId, System.Globalization.CultureInfo.InvariantCulture)),
            r.Status,
            decimal.Parse(r.Total, System.Globalization.CultureInfo.InvariantCulture),
            r.Currency);
    }

    // Sqlite-flavour idempotent upsert. Postgres dialect support lands in a
    // later task when the second provider is added.
    [Command(
        "INSERT INTO \"order_listings\" (\"Id\", \"CustomerId\", \"Status\", \"Total\", \"Currency\") " +
        "VALUES (@id, @customerId, @status, @total, @currency) " +
        "ON CONFLICT(\"Id\") DO UPDATE SET " +
        "\"CustomerId\" = excluded.\"CustomerId\", " +
        "\"Status\" = excluded.\"Status\", " +
        "\"Total\" = excluded.\"Total\", " +
        "\"Currency\" = excluded.\"Currency\"")]
    private partial Task<int> UpsertCoreAsync(string id, string customerId, string status, string total, string currency, CancellationToken ct);

    [Query("SELECT \"Id\", \"CustomerId\", \"Status\", \"Total\", \"Currency\" FROM \"order_listings\" WHERE \"Id\" = @id")]
    private partial Task<OrderListingRow?> ReadByIdAsync(string id, CancellationToken ct);

    private sealed record OrderListingRow(string Id, string CustomerId, string Status, string Total, string Currency);
}
