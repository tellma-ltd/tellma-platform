using CollProbe;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Re-baseline probe: a fresh initial migration (new id) pointed at a database that already holds
// the old chain. Usage: Rebaseline "<server part>" <database>
string cs = $"{args[0]};Database={args[1]}";
using ProbeContext ctx = new(new DbContextOptionsBuilder<ProbeContext>().UseSqlServer(cs).Options);
Console.WriteLine($"# Rebaseline, PROBE_STAGE={ProbeContext.Stage}");
Console.WriteLine($"  GetMigrations: [{string.Join(", ", ctx.Database.GetMigrations())}]");
Console.WriteLine($"  GetAppliedMigrations: [{string.Join(", ", ctx.Database.GetAppliedMigrations())}]");
Console.WriteLine($"  GetPendingMigrations: [{string.Join(", ", ctx.Database.GetPendingMigrations())}]");
Console.WriteLine($"  HasPendingModelChanges: {ctx.Database.HasPendingModelChanges()}");
try
{
    ctx.Database.Migrate();
    Console.WriteLine("  Migrate() -> succeeded");
}
catch (SqlException e)
{
    Console.WriteLine($"  Migrate() -> SqlException {e.Number}: {e.Message.Split('\n')[0]}");
}

Console.WriteLine($"  GetAppliedMigrations after: [{string.Join(", ", ctx.Database.GetAppliedMigrations())}]");
