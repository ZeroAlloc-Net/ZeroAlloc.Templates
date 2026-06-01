using System.Data.Async;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.ORM;

namespace MyApp.Infrastructure.Persistence;

[Scoped]
public sealed partial class OrderRepository(IAsyncDbConnection conn) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        var orderId = await InsertOrderAsync(
            order.CustomerId.Value,
            order.Status.ToString(),
            MoneyConverter.ToStorage(order.Total),
            ct).ConfigureAwait(false);

        foreach (var line in order.Lines)
        {
            await InsertOrderLineAsync(
                orderId,
                line.Sku,
                line.Quantity,
                MoneyConverter.ToStorage(line.Price),
                ct).ConfigureAwait(false);
        }
    }

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        var tuple = await ReadOrderAsync(id.Value, ct).ConfigureAwait(false);
        if (tuple is null) return null;

        var (head, lines) = tuple.Value;
        var orderLines = new List<OrderLine>(lines.Count);
        foreach (var ln in lines)
        {
            orderLines.Add(new OrderLine(ln.Sku, ln.Quantity, MoneyConverter.FromStorage(ln.Price)));
        }

        return Order.Materialize(
            id,
            new CustomerId(head.CustomerId),
            Enum.Parse<OrderStatus>(head.Status),
            MoneyConverter.FromStorage(head.Total),
            orderLines);
    }

    [Query("SELECT COUNT(*) FROM \"Orders\"")]
    public partial Task<int> CountAsync(CancellationToken ct);

    [Command(
        "INSERT INTO \"Orders\" (\"CustomerId\", \"Status\", \"Total\") VALUES (@customerId, @status, @total)",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertOrderAsync(int customerId, string status, string total, CancellationToken ct);

    [Command(
        "INSERT INTO \"OrderLines\" (\"OrderId\", \"Sku\", \"Quantity\", \"Price\") VALUES (@orderId, @sku, @quantity, @price)")]
    public partial Task<int> InsertOrderLineAsync(int orderId, string sku, int quantity, string price, CancellationToken ct);

    [Query(
        "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
        "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;")]
    public partial Task<(OrderHeadRow Head, IReadOnlyList<OrderLineRow> Lines)?> ReadOrderAsync(int id, CancellationToken ct);

    public sealed record OrderHeadRow(int CustomerId, string Status, string Total);
    public sealed record OrderLineRow(string Sku, int Quantity, string Price);
}
