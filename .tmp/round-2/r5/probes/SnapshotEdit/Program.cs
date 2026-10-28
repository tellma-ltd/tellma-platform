using CollProbe;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Snapshot-edit probe: the stage-0 Initial unchanged, the snapshot hand-edited to carry
// UseCollation, no collation migration. Usage: SnapshotEdit "<server part>" <database>
string cs = $"{args[0]};Database={args[1]}";
List<string> log = [];
using (ProbeContext ctx = new(new DbContextOptionsBuilder<ProbeContext>().UseSqlServer(cs).LogTo(log.Add, Microsoft.Extensions.Logging.LogLevel.Information).Options))
{
    Console.WriteLine($"# SnapshotEdit, PROBE_STAGE={ProbeContext.Stage}");
    Console.WriteLine($"  GetMigrations: [{string.Join(", ", ctx.Database.GetMigrations())}]");
    Console.WriteLine($"  HasPendingModelChanges: {ctx.Database.HasPendingModelChanges()}");
    try
    {
        ctx.Database.Migrate();
        Console.WriteLine("  Migrate() -> succeeded");
    }
    catch (Exception e)
    {
        Console.WriteLine($"  Migrate() -> {e.GetType().Name}: {e.Message.Split('\n')[0]}");
    }
}

foreach (string line in log.Where(l => l.Contains("DATABASE", StringComparison.Ordinal) || l.TrimStart().StartsWith("warn:", StringComparison.Ordinal)))
{
    Console.WriteLine("    log: " + string.Join(" | ", line.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("Executed DbCommand", StringComparison.Ordinal))));
}

SqlConnection.ClearAllPools();
using SqlConnection conn = new(cs);
conn.Open();
using SqlCommand cmd = conn.CreateCommand();
cmd.CommandText = "SELECT CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation'))";
Console.WriteLine($"  [{args[1]}] collation={cmd.ExecuteScalar()}");
