using System.Data.Common;
using Npgsql;

namespace MyApp.Persistence;

/// <summary>
/// Npgsql connection-string defaults for throughput. What each one is worth here is measured in
/// https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/blob/main/docs/benchmarks/2026-10-22-template-capacity.md.
/// </summary>
public static class NpgsqlConnectionStrings
{
    /// <summary>
    /// Turns on two Npgsql settings that the connection string does not set itself. Both are off
    /// by default in Npgsql.
    /// <list type="bullet">
    /// <item><c>Max Auto Prepare</c>: Npgsql prepares a statement once it has run a few times on a
    /// physical connection, so the server skips parse and plan on every later call. Every
    /// statement in this app repeats.</item>
    /// <item><c>Multiplexing</c>: commands from concurrent requests share physical connections and
    /// are written to the socket in batches, instead of each request holding a connection for
    /// its whole round trip. It needs the async Npgsql API throughout, which ZA.ORM uses, and it
    /// keeps no session state between commands: no <c>SET</c>, temporary tables or
    /// <c>LISTEN</c> outside a transaction. Add <c>Multiplexing=false</c> to the connection
    /// string if your code needs those.</item>
    /// </list>
    /// </summary>
    public static string WithThroughputDefaults(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // The normalised string lists only the keys that were set, under their canonical names,
        // so a synonym such as MaxAutoPrepare still counts as set.
        var explicitlySet = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString };
        if (!explicitlySet.ContainsKey("Max Auto Prepare"))
        {
            builder.MaxAutoPrepare = 20;
        }
        if (!explicitlySet.ContainsKey("Multiplexing"))
        {
            builder.Multiplexing = true;
        }
        return builder.ConnectionString;
    }
}
