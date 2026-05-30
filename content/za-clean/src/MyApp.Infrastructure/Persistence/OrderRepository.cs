using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MyApp.Application;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;

namespace MyApp.Infrastructure.Persistence;

[Scoped]
public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        // EF Core's INSERT path works under NativeAOT once the compiled model
        // is in place — no LINQ-to-SQL translation is involved.
        await db.Orders.AddAsync(order, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct)
    {
        // Raw SQL because EF Core 10's LINQ-to-SQL translation requires
        // `--precompile-queries` under NativeAOT, and the precompile-queries
        // tooling is currently blocked by source-generator interactions in
        // this template's stack (ZA.Rest's generated code is invisible to
        // the EF Roslyn precompile pass). See docs/za-clean.md
        // "Known limitations under NativeAOT" for the full rationale.

        var conn = db.Database.GetDbConnection();
        // Don't close a connection the caller (or EF) opened for us — only
        // close what we opened. The integration-test factory shares a kept-
        // alive in-memory SQLite connection across the whole test session.
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere)
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            return await ReadOrderAsync(conn, id, ct).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<Order?> ReadOrderAsync(DbConnection conn, OrderId id, CancellationToken ct)
    {
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Single round-trip: head + lines are batched into one command and
            // walked via NextResultAsync. Both Microsoft.Data.Sqlite and Npgsql
            // send the two statements in one network exchange, so this halves the
            // per-read DB latency versus issuing two separate commands.
            //
            // ADO.NET parameter prefix: `@` is accepted by both Microsoft.Data.Sqlite
            // (which also accepts `$` and `:`) AND Npgsql (which rejects `$` because
            // Postgres uses `$1`/`$2` for positional parameters in raw SQL). Using
            // `@id` keeps this raw-SQL repository provider-agnostic. A single named
            // parameter reused across both statements binds correctly on both
            // providers.
            // Double-quoted identifiers preserve casing across providers. Postgres
            // folds unquoted identifiers to lowercase (so `Orders` becomes `orders`,
            // which won't match the schema.postgres.sql-created `"Orders"` table).
            // Sqlite accepts double-quoted identifiers in SELECT lists transparently.
            cmd.CommandText =
                "SELECT \"CustomerId\", \"Status\", \"Total\" FROM \"Orders\" WHERE \"Id\" = @id;" +
                "SELECT \"Sku\", \"Quantity\", \"Price\" FROM \"OrderLines\" WHERE \"OrderId\" = @id;";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = id.Value;
            cmd.Parameters.Add(pId);

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                // Result set 1 — order head.
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }
                var customerId = reader.GetInt32(0);
                // Status round-trip mirrors the EF converter (HasConversion<string>()).
                var status = Enum.Parse<OrderStatus>(reader.GetString(1));
                var total = MoneyConverter.FromStorage(reader.GetString(2));

                // Result set 2 — order lines.
                var lines = new List<OrderLine>();
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var sku = reader.GetString(0);
                        var quantity = reader.GetInt32(1);
                        var price = MoneyConverter.FromStorage(reader.GetString(2));
                        lines.Add(new OrderLine(sku, quantity, price));
                    }
                }

                return Order.Materialize(
                    id,
                    new CustomerId(customerId),
                    status,
                    total,
                    lines);
            }
        }
    }
}
