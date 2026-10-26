# Runs the tree-rollup benchmark matrix produced by gen.py against the throwaway container.
# Usage: pwsh run.ps1 -Plan validate|1m|10m [-Setup]
param(
    [Parameter(Mandatory)] [string] $Plan,
    [string] $Only = '',
    [switch] $Setup,
    [string] $Server = 'localhost,14339',
    [string] $PasswordFile = "$env:TEMP\claude\c--Users-ahmad-workspace-tellma-tellma-platform\bac25519-ee6b-4008-9c32-f578611d3613\scratchpad\sa.txt"
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$pw = Get-Content $PasswordFile -Raw

# (Re)create the database and generators.
if ($Setup) {
    sqlcmd -S $Server -U sa -P $pw -C -f 65001 -b -i (Join-Path $here '00-setup.sql') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'setup failed' }
}

$genArgs = @((Join-Path $here 'gen.py'), $Plan) + $(if ($Only) { @($Only) } else { @() })
$manifest = python @genArgs | ConvertFrom-Json
$cs = "Server=$Server;Database=TreeRollupBench;User Id=sa;Password=$pw;TrustServerCertificate=true;Connect Timeout=30"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

function Invoke-NonQuery([string] $sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 0
    [void] $cmd.ExecuteNonQuery()
}

# Executes a statement, drains every result set, returns (elapsed ms, rows) and optionally the rows.
function Invoke-Reader([string] $sql, [switch] $Collect) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $reader = $cmd.ExecuteReader()
    $rows = 0; $data = @()
    do {
        while ($reader.Read()) {
            $rows++
            if ($Collect) {
                $o = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) { $o[$reader.GetName($i)] = $reader.GetValue($i) }
                $data += [pscustomobject] $o
            }
        }
    } while ($reader.NextResult())
    $reader.Close(); $sw.Stop()
    return [pscustomobject] @{ Ms = $sw.Elapsed.TotalMilliseconds; Rows = $rows; Data = $data }
}

# Query Store's view of one tagged statement: CPU, duration, reads, memory grant, tempdb, DOP.
function Get-QueryStoreStats([string] $tag) {
    $sql = @"
SELECT SUM(rs.count_executions) AS execs,
       SUM(rs.avg_cpu_time * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS cpu_ms,
       SUM(rs.avg_duration * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS dur_ms,
       SUM(rs.avg_logical_io_reads * rs.count_executions) / SUM(rs.count_executions) AS reads,
       MAX(rs.max_query_max_used_memory) * 8 AS mem_kb,
       MAX(rs.max_tempdb_space_used) * 8 AS tempdb_kb,
       MAX(rs.last_dop) AS dop
FROM sys.query_store_query_text AS qt
JOIN sys.query_store_query AS q ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan AS p ON p.query_id = q.query_id
JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
WHERE qt.query_sql_text LIKE '%$tag%';
"@
    return (Invoke-Reader $sql -Collect).Data[0]
}

$results = Join-Path $here "results-$Plan.csv"
$validation = Join-Path $here "validation-$Plan.csv"

# Accounts (the second tree) is built once.
$a = $manifest.accounts
Invoke-NonQuery "EXEC dbo.BuildTree @Target = N'Accounts', @Depth = $($a.depth), @Branch = $($a.branch), @LeafPct = $($a.leafPct), @IdBase = 1000000;"

$built = ''
foreach ($s in $manifest.scenarios) {
    # Rebuild the center tree and the facts only when the tree or the size changes.
    $key = "$($s.tree)/$($s.n)"
    if ($key -ne $built) {
        $t = $s.treeSpec
        Write-Host "[$(Get-Date -Format HH:mm:ss)] building $key"
        Invoke-NonQuery "EXEC dbo.BuildTree @Target = N'Centers', @Depth = $($t.depth), @Branch = $($t.branch), @LeafPct = $($t.leafPct), @IdBase = 0;"
        Invoke-NonQuery "EXEC dbo.BuildFacts @N = $($s.n), @NullPct = 2;"
        $built = $key
    }

    if ($s.validate) {
        $v = Invoke-Reader $s.validate -Collect
        foreach ($row in $v.Data) {
            $row | Add-Member scenario $s.id -PassThru | Export-Csv $validation -Append -NoTypeInformation
            Write-Host ("  {0,-24} {1,-5} rows={2,7} dup={3} mismatch={4} truth={5}" -f $s.id, $row.variant, $row.rows_returned, $row.dup_cells, $row.mismatches, $row.truth_cells)
        }
        continue
    }

    foreach ($var in $s.variants) {
        $warm = $var.sql.Replace('/*TAG*/', "/* bench:$($s.id):$($var.name):w */")
        $meas = $var.sql.Replace('/*TAG*/', "/* bench:$($s.id):$($var.name):m */")
        # Slow reference variants run once, without a warm-up (the data is already cached by then).
        $runs = if ($var.runs) { $var.runs } else { $s.runs }
        if (-not $var.runs) { [void] (Invoke-Reader $warm) }
        $times = @(); $rows = 0
        for ($i = 0; $i -lt $runs; $i++) { $r = Invoke-Reader $meas; $times += $r.Ms; $rows = $r.Rows }
        $sorted = $times | Sort-Object
        $qs = Get-QueryStoreStats "/* bench:$($s.id):$($var.name):m */"
        $line = [pscustomobject] @{
            scenario = $s.id; tree = $s.tree; n = $s.n; col = $s.col; variant = $var.name; runs = $runs
            client_ms_median = [math]::Round($sorted[[int][math]::Floor($sorted.Count / 2)], 1)
            client_ms_min = [math]::Round($sorted[0], 1); rows = $rows
            cpu_ms = [math]::Round([double]$qs.cpu_ms, 1); dur_ms = [math]::Round([double]$qs.dur_ms, 1)
            reads = [math]::Round([double]$qs.reads); mem_kb = $qs.mem_kb; tempdb_kb = $qs.tempdb_kb; dop = $qs.dop
        }
        $line | Export-Csv $results -Append -NoTypeInformation
        Write-Host ("  {0,-24} {1,-5} med={2,9} ms cpu={3,9} ms rows={4,7} mem={5,8} KB tempdb={6} dop={7}" -f $s.id, $var.name, $line.client_ms_median, $line.cpu_ms, $rows, $line.mem_kb, $line.tempdb_kb, $line.dop)
    }
}
$conn.Close()
