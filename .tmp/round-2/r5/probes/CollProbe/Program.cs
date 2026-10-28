using CollProbe;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Probe for the r5 identity facts. Usage:
//   CollProbe ops
//   CollProbe scenarios "<server part of a connection string>" <label>
//   CollProbe pending-check "<server part>"
//   CollProbe keep "<server part>" <database>      (Migrate() a missing database and keep it)
//   CollProbe dbinfo "<server part>" <database>
//   CollProbe drop "<server part>" <database>
string command = args.Length > 0 ? args[0] : "ops";
Console.WriteLine($"# CollProbe {command}, PROBE_STAGE={ProbeContext.Stage}");

switch (command)
{
    case "ops":
        Ops();
        break;
    case "scenarios":
        Scenarios(args[1], args[2]);
        break;
    case "pending-check":
        PendingCheck(args[1]);
        break;
    case "keep":
        Keep(args[1], args[2]);
        break;
    case "dbinfo":
        DbInfo(args[1], args[2]);
        break;
    case "drop":
        Drop(args[1], args[2]);
        break;
    default:
        throw new ArgumentException(command);
}

// Lists every migration's Up/Down operations: the shape of a "no migration alters the database
// collation" test. Needs no database.
static void Ops()
{
    using ProbeContext ctx = Create(@"Server=(localdb)\MSSQLLocalDB;Database=Unused;Integrated Security=true", null);
    IMigrationsAssembly assembly = ctx.GetService<IMigrationsAssembly>();
    string provider = ctx.Database.ProviderName!;
    foreach ((string id, System.Reflection.TypeInfo type) in assembly.Migrations)
    {
        Migration migration = assembly.CreateMigration(type, provider);
        Console.WriteLine($"  {id}: Up [{Describe(migration.UpOperations)}] Down [{Describe(migration.DownOperations)}]");
        Console.WriteLine($"    target model collation: {migration.TargetModel?.GetCollation() ?? "<null>"}");
    }

    Console.WriteLine($"  snapshot collation: {assembly.ModelSnapshot?.Model.GetCollation() ?? "<null>"}");
    Console.WriteLine($"  HasPendingModelChanges(): {ctx.Database.HasPendingModelChanges()}");
}

static string Describe(IReadOnlyList<MigrationOperation> ops) =>
    string.Join(", ", ops.Select(op => op is AlterDatabaseOperation a
        ? $"AlterDatabase(Collation={a.Collation ?? "<null>"}, Old={a.OldDatabase.Collation ?? "<null>"})"
        : op.GetType().Name));

// (a), (a'), (b1), (b2) on one server.
static void Scenarios(string server, string label)
{
    string suffix = Guid.NewGuid().ToString("N")[..6];

    // (a) A missing database: the startup-check APIs, then Migrate().
    string db = $"CollProbeA_{suffix}";
    Console.WriteLine($"=== ({label}) a. Migrate() on a missing database {db}");
    {
        List<string> log = [];
        using ProbeContext ctx = Create(Cs(server, db), log);
        Console.WriteLine($"  CanConnect: {ctx.Database.CanConnect()}");
        Console.WriteLine($"  GetAppliedMigrations: [{string.Join(", ", ctx.Database.GetAppliedMigrations())}]");
        Console.WriteLine($"  GetPendingMigrations: [{string.Join(", ", ctx.Database.GetPendingMigrations())}]");
        Console.WriteLine($"  HasPendingModelChanges: {ctx.Database.HasPendingModelChanges()}");
        Run(() => ctx.Database.Migrate());
        PrintLog(log);
        Console.WriteLine($"  GetPendingMigrations after: [{string.Join(", ", ctx.Database.GetPendingMigrations())}]");
    }

    DbInfo(server, db);
    Drop(server, db);

    // (a') An operator-created database in the platform collation, then Migrate().
    db = $"CollProbeA2_{suffix}";
    Console.WriteLine($"=== ({label}) a'. Migrate() on an existing empty database created COLLATE Latin1_General_100_CI_AS");
    Master(server, $"CREATE DATABASE [{db}] COLLATE Latin1_General_100_CI_AS;");
    {
        List<string> log = [];
        using ProbeContext ctx = Create(Cs(server, db), log);
        Run(() => ctx.Database.Migrate());
        PrintLog(log);
    }

    DbInfo(server, db);
    Drop(server, db);

    // (b1) An existing empty database in the server default.
    db = $"CollProbeB1_{suffix}";
    Console.WriteLine($"=== ({label}) b1. Migrate() on an existing empty database in the server default");
    Master(server, $"CREATE DATABASE [{db}];");
    {
        List<string> log = [];
        using ProbeContext ctx = Create(Cs(server, db), log);
        Run(() => ctx.Database.Migrate());
        PrintLog(log);
    }

    DbInfo(server, db);
    Drop(server, db);

    // (b2) An existing database in the server default that already has the first migration
    // (a developer's database from before the collation change), then Migrate().
    db = $"CollProbeB2_{suffix}";
    Console.WriteLine($"=== ({label}) b2. server-default database with the first migration applied, then Migrate()");
    Master(server, $"CREATE DATABASE [{db}];");
    {
        List<string> log = [];
        using ProbeContext ctx = Create(Cs(server, db), log);
        string first = ctx.Database.GetMigrations().First();
        Run(() => ctx.GetService<IMigrator>().Migrate(first));
        Console.WriteLine($"  after Migrate(\"{first}\"): pending [{string.Join(", ", ctx.Database.GetPendingMigrations())}]");
        log.Clear();
        Run(() => ctx.Database.Migrate());
        PrintLog(log);
    }

    DbInfo(server, db);
    Drop(server, db);
}

// A model change without its migration: what Migrate() and HasPendingModelChanges() report.
static void PendingCheck(string server)
{
    string db = $"CollProbeP_{Guid.NewGuid().ToString("N")[..6]}";
    Console.WriteLine($"=== pending-check on missing database {db}");
    List<string> log = [];
    using (ProbeContext ctx = Create(Cs(server, db), log))
    {
        Console.WriteLine($"  HasPendingModelChanges: {ctx.Database.HasPendingModelChanges()}");
        Run(() => ctx.Database.Migrate());
        PrintLog(log);
        Console.WriteLine($"  database exists afterwards: {ctx.Database.CanConnect()}");
    }

    Drop(server, db);
}

static void Keep(string server, string db)
{
    List<string> log = [];
    using ProbeContext ctx = Create(Cs(server, db), log);
    Run(() => ctx.Database.Migrate());
    PrintLog(log);
    DbInfo(server, db);
}

static void DbInfo(string server, string db)
{
    SqlConnection.ClearAllPools();
    using SqlConnection conn = new(Cs(server, db));
    conn.Open();
    using SqlCommand cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')),
               (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()),
               (SELECT STRING_AGG(s.name + '.' + t.name, ', ') FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = '__EFMigrationsHistory'),
               (SELECT c.collation_name FROM sys.columns c WHERE c.object_id = OBJECT_ID('idsvr.Items') AND c.name = 'Name')
        """;
    using (SqlDataReader r = cmd.ExecuteReader())
    {
        r.Read();
        Console.WriteLine($"  [{db}] collation={r.GetString(0)} rcsi={r.GetBoolean(1)} history={(r.IsDBNull(2) ? "<none>" : r.GetString(2))} Items.Name={(r.IsDBNull(3) ? "<none>" : r.GetString(3))}");
    }

    cmd.CommandText = "IF OBJECT_ID('dbo.__EFMigrationsHistory') IS NOT NULL SELECT STRING_AGG(MigrationId, ', ') FROM dbo.__EFMigrationsHistory ELSE SELECT CAST(NULL AS nvarchar(max))";
    object? rows = cmd.ExecuteScalar();
    Console.WriteLine($"  [{db}] dbo.__EFMigrationsHistory rows: {(rows is DBNull or null ? "<none>" : rows)}");
}

static void Drop(string server, string db)
{
    SqlConnection.ClearAllPools();
    Master(server, $"IF DB_ID('{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
}

static void Master(string server, string sql)
{
    using SqlConnection conn = new(Cs(server, "master"));
    conn.Open();
    using SqlCommand cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static string Cs(string server, string db) => $"{server};Database={db}";

static ProbeContext Create(string cs, List<string>? log)
{
    DbContextOptionsBuilder<ProbeContext> o = new();
    o.UseSqlServer(cs);
    if (log is not null)
    {
        o.LogTo(log.Add, Microsoft.Extensions.Logging.LogLevel.Information);
    }

    return new ProbeContext(o.Options);
}

static void Run(Action action)
{
    try
    {
        action();
        Console.WriteLine("  -> succeeded");
    }
    catch (Exception e)
    {
        Console.WriteLine($"  -> {e.GetType().Name}: {e.Message.Split('\n')[0]}");
        if (e is SqlException s)
        {
            Console.WriteLine($"     SqlException Number {s.Number}");
        }
    }
}

// Prints every logged statement that touches a database's collation or creation, and every warning.
static void PrintLog(List<string> log)
{
    foreach (string line in log)
    {
        bool isWarning = line.TrimStart().StartsWith("warn:", StringComparison.Ordinal);
        bool touchesDatabase = line.Contains("CREATE DATABASE", StringComparison.Ordinal)
            || line.Contains("ALTER DATABASE", StringComparison.Ordinal)
            || line.Contains("COLLATE", StringComparison.Ordinal)
            || line.Contains("Applying migration", StringComparison.Ordinal)
            || line.Contains("PendingModelChanges", StringComparison.Ordinal);
        if (isWarning || touchesDatabase)
        {
            string flat = string.Join(" | ", line.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("Executed DbCommand", StringComparison.Ordinal)));
            Console.WriteLine($"    log: {(flat.Length > 400 ? flat[..400] + "…" : flat)}");
        }
    }
}
