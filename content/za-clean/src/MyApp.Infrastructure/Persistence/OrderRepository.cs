using System.Data;
using System.Data.Async;
using MyApp.Application;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.ORM;

namespace MyApp.Infrastructure.Persistence;

[Scoped]
public sealed partial class OrderRepository(IAsyncDbConnection conn) : IOrderRepository
{
    public async Task<Order> AddAsync(Order order, CancellationToken ct)
    {
        // BeginTransactionAsync requires an open connection. The per-command
        // ref-counted prologue inside each emitted partial method sees
        // State == Open and is a no-op for both connection lifecycle and
        // transaction state — the tx attaches via cmd.Transaction (v1.5+),
        // not via auto-bind, so SqlClient works correctly too.
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere) await conn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var orderId = await InsertOrderAsync(
                    order.CustomerId.Value,
                    order.Status.ToString(),
                    MoneyConverter.ToStorage(order.Total),
                    tx,
                    ct).ConfigureAwait(false);

                foreach (var line in order.Lines)
                {
                    await InsertOrderLineAsync(
                        orderId,
                        line.Sku,
                        line.Quantity,
                        MoneyConverter.ToStorage(line.Price),
                        tx,
                        ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                // Exception above propagates out of the `using` scope — tx
                // DisposeAsync rolls back if CommitAsync wasn't reached.
                return Order.Materialize(
                    new OrderId(orderId),
                    order.CustomerId,
                    order.Status,
                    order.Total,
                    order.Lines);
            }
        }
        finally
        {
            if (openedHere) await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public async Task<OrderReadModel?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var tuple = await ReadOrderAsync(id.Value, ct).ConfigureAwait(false);
        if (tuple is null) return null;

        var (head, lines) = tuple.Value;
        var lineModels = new OrderLineReadModel[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            lineModels[i] = new OrderLineReadModel(
                lines[i].Sku,
                lines[i].Quantity,
                MoneyConverter.AmountFromStorage(lines[i].Price));
        }

        var total = MoneyConverter.FromStorage(head.Total);
        return new OrderReadModel(
            id,
            new CustomerId(head.CustomerId),
            head.Status,
            total.Amount,
            total.Currency,
            lineModels);
    }

    [Query("SELECT COUNT(*) FROM \"Orders\"")]
    public partial Task<int> CountAsync(CancellationToken ct);

    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Status\", \"Total\") VALUES (@customerId, @status, @total) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    private partial Task<int> InsertOrderAsync(int customerId, string status, string total, IAsyncDbTransaction tx, CancellationToken ct);

    [Command(
        "INSERT INTO \"OrderLines\" (\"OrderId\", \"Sku\", \"Quantity\", \"Price\") VALUES (@orderId, @sku, @quantity, @price)")]
    private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, IAsyncDbTransaction tx, CancellationToken ct);

    [Query(
        "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
        "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
    private partial Task<(OrderHeadRow Head, IReadOnlyList<OrderLineRow> Lines)?> ReadOrderAsync(int id, CancellationToken ct);

    private sealed record OrderHeadRow(int CustomerId, string Status, string Total);
    private sealed record OrderLineRow(string Sku, int Quantity, string Price);
}
