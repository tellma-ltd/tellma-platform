#:package Microsoft.Data.SqlClient@6.1.1
// Times the "C in C#" variant end to end: the leaf-grain GROUP BY, the member trees, and the
// in-memory rollup of every leaf row into all (row ancestor, column ancestor) cells with the
// self twins the cube builder needs.
// Usage: dotnet run rollup.cs -- <none|quarter|accounts> <runs>
using System.Diagnostics;
using Microsoft.Data.SqlClient;

var col = args.Length > 0 ? args[0] : "accounts";
var runs = args.Length > 1 ? int.Parse(args[1]) : 5;
var pw = File.ReadAllText(Path.Combine(Path.GetTempPath(), "claude",
    "c--Users-ahmad-workspace-tellma-tellma-platform", "bac25519-ee6b-4008-9c32-f578611d3613", "scratchpad", "sa.txt"));
var cs = $"Server=localhost,14339;Database=TreeRollupBench;User Id=sa;Password={pw};TrustServerCertificate=true";
const int All = -1, None = -2;

var colKey = col switch { "quarter" => "DATEPART(QUARTER, T.PostingDate)", "accounts" => "T.AccountId", _ => "NULL" };
var leafSql = $"SELECT T.CenterId, {colKey}, SUM(T.Amount), COUNT_BIG(*) FROM dbo.Lines AS T GROUP BY T.CenterId, {colKey}";

var results = new List<(double sql, double trees, double rollup, int cells)>();
for (var run = 0; run <= runs; run++)
{
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    // 1. Leaf-grain aggregate (the only fact scan).
    var sw = Stopwatch.StartNew();
    var leaves = new List<(int? r, int c, decimal s, long n)>();
    await using (var cmd = new SqlCommand(leafSql, conn) { CommandTimeout = 0 })
    await using (var rd = await cmd.ExecuteReaderAsync())
        while (await rd.ReadAsync())
            leaves.Add((rd.IsDBNull(0) ? null : rd.GetInt32(0), rd.IsDBNull(1) ? All : Convert.ToInt32(rd.GetValue(1)),
                rd.GetDecimal(2), rd.GetInt64(3)));
    var tSql = sw.Elapsed.TotalMilliseconds;

    // 2. Member trees (id, parent): in production, the same statement batch or the cache.
    sw.Restart();
    var centers = await LoadTree(conn, "Centers");
    var accounts = col == "accounts" ? await LoadTree(conn, "Accounts") : null;
    var tTrees = sw.Elapsed.TotalMilliseconds;

    // 3. Rollup: each leaf row adds into every (row ancestor or ALL) x (column ancestor or ALL) cell.
    // Accumulators: total, row-self twin, column-self twin, both-self twin.
    sw.Restart();
    var rowChain = Chains(centers);
    var colChain = accounts is null ? null : Chains(accounts);
    var cells = new Dictionary<(int, int), Acc>();
    foreach (var (r, c, s, n) in leaves)
    {
        var rs = r is int ri ? rowChain[ri] : [None];
        int[] cc = colChain is not null ? colChain[c] : c == All ? [] : [c];
        for (var i = 0; i <= rs.Length; i++)
        {
            var rk = i < rs.Length ? rs[i] : All;
            var rSelf = i == 0;
            for (var j = 0; j <= cc.Length; j++)
            {
                var ck = j < cc.Length ? cc[j] : All;
                var cSelf = j == 0 || colChain is null;
                ref var a = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(cells, (rk, ck), out _);
                a.S += s; a.N += n;
                if (rSelf) { a.RS += s; a.RN += n; }
                if (cSelf) { a.CS += s; a.CN += n; }
                if (rSelf && cSelf) { a.BS += s; a.BN += n; }
            }
        }
    }
    var tRollup = sw.Elapsed.TotalMilliseconds;
    if (run > 0) results.Add((tSql, tTrees, tRollup, cells.Count)); // run 0 warms up
}

var med = (Func<(double sql, double trees, double rollup, int cells), double> f) => results.Select(f).Order().ElementAt(results.Count / 2);
Console.WriteLine($"leaf SQL {med(x => x.sql):F0} ms | trees {med(x => x.trees):F0} ms | rollup {med(x => x.rollup):F0} ms | " +
    $"total {med(x => x.sql + x.trees + x.rollup):F0} ms | cells {results[0].cells}");

static async Task<Dictionary<int, int?>> LoadTree(SqlConnection conn, string table)
{
    var tree = new Dictionary<int, int?>();
    await using var cmd = new SqlCommand($"SELECT Id, ParentId FROM dbo.{table}", conn);
    await using var rd = await cmd.ExecuteReaderAsync();
    while (await rd.ReadAsync()) tree[rd.GetInt32(0)] = rd.IsDBNull(1) ? null : rd.GetInt32(1);
    return tree;
}

// Self first, then each ancestor up to the root.
static Dictionary<int, int[]> Chains(Dictionary<int, int?> tree)
{
    var chains = new Dictionary<int, int[]>(tree.Count);
    foreach (var id in tree.Keys)
    {
        var chain = new List<int>();
        for (int? x = id; x is int v; x = tree[v]) chain.Add(v);
        chains[id] = [.. chain];
    }
    return chains;
}

struct Acc { public decimal S, RS, CS, BS; public long N, RN, CN, BN; }
