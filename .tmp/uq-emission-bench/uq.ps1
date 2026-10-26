# Benchmarks spec 0010 §F.13's 'uq' emission forms for non-row-grain operands (Part F review flag 4).
# Usage: pwsh uq.ps1 [-Runs 7]
param(
    [int] $Runs = 7,
    [string] $Server = 'localhost,14339',
    [string] $PasswordFile = "$env:TEMP\claude\c--Users-ahmad-workspace-tellma-tellma-platform\bac25519-ee6b-4008-9c32-f578611d3613\scratchpad\sa.txt"
)
$ErrorActionPreference = 'Stop'
$pw = Get-Content $PasswordFile -Raw
$conn = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=UqBench;User Id=sa;Password=$pw;TrustServerCertificate=true"
$conn.Open()

$today = [datetime] '2025-06-15'
# One-row backward seek on (Calendar, StartDate): the month holding @today (§F.13's lookup).
$monthOf = "SELECT TOP (1) M.[Year], M.[Month], M.StartDate FROM dbo.CalendarMonths AS M WHERE M.Calendar = @cal AND M.StartDate <= @today AND @today < M.EndDate ORDER BY M.StartDate DESC"
$somScalar = "(SELECT TOP (1) M.StartDate FROM dbo.CalendarMonths AS M WHERE M.Calendar = @cal AND M.StartDate <= @today AND @today < M.EndDate ORDER BY M.StartDate DESC)"
# startOfYear: the second lookup, an equality seek on (Calendar, Year, Month = 1), nested in the scalar form.
$soyScalar = "(SELECT Y.StartDate FROM dbo.CalendarMonths AS Y WHERE Y.Calendar = @cal AND Y.[Month] = 1 AND Y.[Year] = (SELECT TOP (1) M.[Year] FROM dbo.CalendarMonths AS M WHERE M.Calendar = @cal AND M.StartDate <= @today AND @today < M.EndDate ORDER BY M.StartDate DESC))"
$gcSom = "DATEADD(MONTH, DATEDIFF(MONTH, CONVERT(date, '00010101'), @today), CONVERT(date, '00010101'))"
$gcSoy = "DATEADD(YEAR, DATEDIFF(YEAR, CONVERT(date, '00010101'), @today), CONVERT(date, '00010101'))"

function Q1([string] $tag, [string] $where, [string] $from = 'dbo.Lines AS T', [string] $opt = '') {
    "SELECT /* $tag */ T.CustomerId, SUM(T.Amount) AS s, COUNT_BIG(*) AS n FROM $from WHERE $where GROUP BY T.CustomerId $opt"
}

# The query matrix: (query, variant, sql, @from value or $null).
function Get-Cases([string] $phase) {
    $cases = @()
    foreach ($range in 'MTD', 'YTD') {
        $scalar = if ($range -eq 'MTD') { $somScalar } else { $soyScalar }
        $bound = if ($range -eq 'MTD') { [datetime] '2025-05-28' } else { [datetime] '2024-07-07' }
        $q = "Q1-$range"; $t = { param($v) "uq:${phase}:${q}:$v" }
        $lateral = if ($range -eq 'MTD') {
            "dbo.Lines AS T OUTER APPLY ($monthOf) AS C1"
        } else {
            "dbo.Lines AS T OUTER APPLY ($monthOf) AS C1 OUTER APPLY (SELECT Y.StartDate FROM dbo.CalendarMonths AS Y WHERE Y.Calendar = @cal AND Y.[Year] = C1.[Year] AND Y.[Month] = 1) AS C2"
        }
        $lateralCol = if ($range -eq 'MTD') { 'C1.StartDate' } else { 'C2.StartDate' }
        $cases += [pscustomobject] @{ q = $q; v = 'S'; sql = (Q1 (& $t 'S') "T.PostingDate >= $scalar"); from = $null }
        $cases += [pscustomobject] @{ q = $q; v = 'P'; sql = (Q1 (& $t 'P') 'T.PostingDate >= @from'); from = $bound }
        $cases += [pscustomobject] @{ q = $q; v = 'V'; sql = "DECLARE @v date = $scalar; " + (Q1 (& $t 'V') 'T.PostingDate >= @v'); from = $null }
        $cases += [pscustomobject] @{ q = $q; v = 'VR'; sql = "DECLARE @v date = $scalar; " + (Q1 (& $t 'VR') 'T.PostingDate >= @v' -opt 'OPTION (RECOMPILE)'); from = $null }
        $cases += [pscustomobject] @{ q = $q; v = 'A'; sql = (Q1 (& $t 'A') "T.PostingDate >= $lateralCol" $lateral); from = $null }
        $gc = if ($range -eq 'MTD') { $gcSom } else { $gcSoy }
        $cases += [pscustomobject] @{ q = $q; v = 'G'; sql = (Q1 (& $t 'G') "T.PostingDate >= $gc"); from = $null }
    }
    # Aggregated operand: month(max(PostingDate), 'uq') per group, through a value binding in the subquery.
    $q = 'Q2-agg'
    $base = "SELECT /* uq:${phase}:${q}:{V} */ T.CustomerId, SUM(T.Amount) AS s, MAX(T.PostingDate) AS d{X} FROM dbo.Lines AS T GROUP BY T.CustomerId"
    $cases += [pscustomobject] @{ q = $q; v = 'base'; sql = $base.Replace('{V}', 'base').Replace('{X}', ''); from = $null }
    $cases += [pscustomobject] @{ q = $q; v = 'S'; sql = $base.Replace('{V}', 'S').Replace('{X}', ", (SELECT TOP (1) M.[Month] FROM (VALUES (MAX(T.PostingDate))) AS b(d) JOIN dbo.CalendarMonths AS M ON M.Calendar = @cal AND M.StartDate <= b.d AND b.d < M.EndDate ORDER BY M.StartDate DESC) AS m"); from = $null }
    $cases += [pscustomobject] @{ q = $q; v = 'G'; sql = $base.Replace('{V}', 'G').Replace('{X}', ', MONTH(MAX(T.PostingDate)) AS m'); from = $null }
    return $cases
}

function Invoke-Case($case) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $case.sql; $cmd.CommandTimeout = 0
    [void] $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter('@today', [System.Data.SqlDbType]::Date) -Property @{ Value = $today }))
    [void] $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter('@cal', [System.Data.SqlDbType]::VarChar, 10) -Property @{ Value = 'uq' }))
    if ($case.from) { [void] $cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter('@from', [System.Data.SqlDbType]::Date) -Property @{ Value = $case.from })) }
    # DataTable.Load drains in compiled code, so the client adds little beyond the transfer.
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $rd = $cmd.ExecuteReader(); $dt = New-Object System.Data.DataTable; $dt.Load($rd); $rd.Close(); $sw.Stop()
    [pscustomobject] @{ Ms = $sw.Elapsed.TotalMilliseconds; Rows = $dt.Rows.Count; Sum = $(if ($dt.Columns.Contains('s')) { $dt.Compute('SUM(s)', '') } else { $null }) }
}

# Query Store: runtime stats plus the plan's access to Lines (operator and estimated rows).
function Get-Stats([string] $tag) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
SELECT SUM(rs.count_executions) AS execs,
       SUM(rs.avg_cpu_time * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS cpu_ms,
       SUM(rs.avg_duration * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS dur_ms,
       SUM(rs.avg_logical_io_reads * rs.count_executions) / SUM(rs.count_executions) AS reads,
       MAX(rs.max_query_max_used_memory) * 8 AS mem_kb, MAX(rs.last_dop) AS dop,
       (SELECT TOP (1) p2.query_plan FROM sys.query_store_plan AS p2 WHERE p2.query_id = MAX(q.query_id) ORDER BY p2.last_execution_time DESC) AS query_plan
FROM sys.query_store_query_text AS qt
JOIN sys.query_store_query AS q ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan AS p ON p.query_id = q.query_id
JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
WHERE qt.query_sql_text LIKE '%/* $tag */%' AND qt.query_sql_text NOT LIKE '%DECLARE%';
"@
    $rd = $cmd.ExecuteReader(); [void] $rd.Read()
    $o = [ordered] @{ cpu_ms = $rd['cpu_ms']; dur_ms = $rd['dur_ms']; reads = $rd['reads']; mem_kb = $rd['mem_kb']; dop = $rd['dop']; access = ''; est = '' }
    $planText = $rd['query_plan']; $rd.Close()
    if ($planText -isnot [DBNull]) {
        [xml] $x = $planText
        $ns = New-Object Xml.XmlNamespaceManager $x.NameTable; $ns.AddNamespace('s', 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
        $ops = $x.SelectNodes("//s:RelOp[s:IndexScan/s:Object[@Table='[Lines]']]", $ns)
        $o.access = ($ops | ForEach-Object { "$($_.PhysicalOp)" }) -join '+'
        $o.est = ($ops | ForEach-Object { [math]::Round([double] $_.EstimateRows) }) -join '+'
    }
    [pscustomobject] $o
}

$out = Join-Path $PSScriptRoot 'results.csv'
Remove-Item $out -ErrorAction SilentlyContinue
$clear = $conn.CreateCommand(); $clear.CommandText = 'ALTER DATABASE UqBench SET QUERY_STORE CLEAR;'; [void] $clear.ExecuteNonQuery()

# P's extra cost: the constant statement (§F.11) as its own round trip.
$const = [pscustomobject] @{ q = 'const'; v = 'rt'; sql = "SELECT /* uq:const */ $somScalar AS c0, $soyScalar AS c1"; from = $null }
$constMs = @(1..30 | ForEach-Object { (Invoke-Case $const).Ms } | Sort-Object)
Write-Host ("constant statement round trip: median {0:F2} ms, p90 {1:F2} ms" -f $constMs[15], $constMs[27])
$phases = [ordered] @{
    cov  = 'CREATE INDEX IX_Lines_PostingDate ON dbo.Lines (PostingDate) INCLUDE (CustomerId, Amount);'
    nc   = 'CREATE INDEX IX_Lines_PostingDate ON dbo.Lines (PostingDate);'
    none = ''
}
foreach ($phase in $phases.Keys) {
    $c = $conn.CreateCommand(); $c.CommandTimeout = 0
    $c.CommandText = "DROP INDEX IF EXISTS IX_Lines_PostingDate ON dbo.Lines; $($phases[$phase]) DBCC FREEPROCCACHE WITH NO_INFOMSGS;"
    [void] $c.ExecuteNonQuery()
    Write-Host "[$(Get-Date -Format HH:mm:ss)] phase $phase"
    foreach ($case in Get-Cases $phase) {
        [void] (Invoke-Case $case)   # warm-up and compile
        $r = @(1..$Runs | ForEach-Object { Invoke-Case $case })
        $med = ($r.Ms | Sort-Object)[[int][math]::Floor($Runs / 2)]
        $st = Get-Stats "uq:${phase}:$($case.q):$($case.v)"
        $line = [pscustomobject] @{
            phase = $phase; query = $case.q; variant = $case.v; ms_median = [math]::Round($med, 1)
            rows = $r[0].Rows; checksum = $r[0].Sum; cpu_ms = [math]::Round([double] $st.cpu_ms, 1)
            dur_ms = [math]::Round([double] $st.dur_ms, 1)
            reads = [math]::Round([double] $st.reads); mem_kb = $st.mem_kb; dop = $st.dop; access = $st.access; est_rows = $st.est
        }
        $line | Export-Csv $out -Append -NoTypeInformation
        Write-Host ("  {0,-5} {1,-7} {2,-4} med={3,7} dur={4,7} cpu={5,7} reads={6,7} rows={7,6} sum={8} access={9} est={10} dop={11}" -f $phase, $case.q, $case.v, $line.ms_median, $line.dur_ms, $line.cpu_ms, $line.reads, $line.rows, $line.checksum, $line.access, $line.est_rows, $line.dop)
    }
}
$conn.Close()
