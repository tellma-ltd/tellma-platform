-- Copyright (c) Tellma Ltd. All rights reserved.
--
-- This source code is licensed under the Apache-2.0 license found in the
-- LICENSE file in the root directory of this source tree.

-- The 'uq' join-shape benchmark: how a path-free Umm al-Qura lookup should reach a filter over a
-- large fact table. It compares three shapes of the same grouped read, in which ten million lines
-- reach their posting date through the invoice header:
--
--   variable   the lookup hoisted into a DECLARE ahead of the statement (the engine's design);
--   parameter  the lookup's answer bound as a parameter, the best a host could do by hand;
--   scalar     the lookup written in place as an uncorrelated scalar subquery;
--   recompile  the hoisted variable again, with the statement recompiled on every run so the
--              optimizer sees the variable's value.
--
-- Each shape runs against two header date indexes, one covering the grouped column and one that is
-- not, and at two selectivities: since the current Hijri month and since the current Hijri year.
-- A measurement, not a suite: run it by hand with sqlcmd. See README.md beside this file.

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ---------------------------------------------------------------------------------------------
-- The database, created once and reused by later runs.
-- ---------------------------------------------------------------------------------------------
IF DB_ID(N'QueryexUqBenchmark') IS NULL
BEGIN
    CREATE DATABASE QueryexUqBenchmark;
    ALTER DATABASE QueryexUqBenchmark SET RECOVERY SIMPLE;
END
GO

USE QueryexUqBenchmark;
GO

IF SCHEMA_ID(N'bench') IS NULL
    EXEC (N'CREATE SCHEMA bench;');
GO

-- ---------------------------------------------------------------------------------------------
-- The month table, shaped and indexed as the platform's core.CalendarMonths.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'bench.CalendarMonths') IS NULL
BEGIN
    CREATE TABLE bench.CalendarMonths
    (
        Calendar varchar(2) NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        StartDate date NOT NULL,
        EndDate date NOT NULL,
        CONSTRAINT PK_CalendarMonths PRIMARY KEY (Calendar, [Year], [Month])
    );

    CREATE UNIQUE INDEX UX_CalendarMonths_Calendar_StartDate ON bench.CalendarMonths (Calendar, StartDate);
END
GO

-- Populated apart from the table's creation, so a run interrupted while loading loads it again.
IF NOT EXISTS (SELECT 1 FROM bench.CalendarMonths)
BEGIN
    -- The months come from the backend's own Hijri conversion (style 131, the tabular Kuwaiti
    -- algorithm), which is not Umm al-Qura but has the same shape: 12 months of 29 or 30 days. The
    -- benchmark measures how the lookup is joined, not which day a month starts on.
    WITH days AS
    (
        SELECT TOP (DATEDIFF(DAY, '19000430', '20771117'))
            DATEADD(DAY, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, CONVERT(date, '19000430')) AS d
        FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b
    ),
    hijri AS
    (
        SELECT d, CONVERT(varchar(30), CONVERT(datetime, d), 131) AS h FROM days
    ),
    starts AS
    (
        SELECT
            d AS StartDate,
            CONVERT(int, SUBSTRING(h, 7, 4)) AS [Year],
            CONVERT(int, SUBSTRING(h, 4, 2)) AS [Month]
        FROM hijri
        WHERE LEFT(h, 2) = ' 1' OR LEFT(h, 2) = '01'
    ),
    months AS
    (
        -- Each month ends where the next begins; the last one found has no end and is dropped.
        SELECT [Year], [Month], StartDate, LEAD(StartDate) OVER (ORDER BY StartDate) AS EndDate
        FROM starts
    )
    INSERT bench.CalendarMonths (Calendar, [Year], [Month], StartDate, EndDate)
    SELECT 'uq', [Year], [Month], StartDate, EndDate
    FROM months
    WHERE EndDate IS NOT NULL;
END
GO

-- ---------------------------------------------------------------------------------------------
-- The fact tables: a million invoice headers over eleven years up to the benchmark's today, ten lines
-- each.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'bench.Invoices') IS NULL
BEGIN
    CREATE TABLE bench.Invoices
    (
        Id int NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
        CenterId int NOT NULL,
        PostingDate date NOT NULL,
        Memo nvarchar(100) NOT NULL
    );

    CREATE TABLE bench.InvoiceLines
    (
        Id int NOT NULL CONSTRAINT PK_InvoiceLines PRIMARY KEY,
        InvoiceId int NOT NULL,
        Amount decimal(19, 4) NOT NULL,
        Notes nvarchar(100) NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM bench.Invoices)
BEGIN
    -- Headers are posted in key order, as a ledger's are, so the dates follow the keys.
    WITH n AS
    (
        SELECT TOP (1000000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
        FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b CROSS JOIN sys.all_columns AS c
    )
    INSERT bench.Invoices WITH (TABLOCK) (Id, CenterId, PostingDate, Memo)
    SELECT
        i,
        (i * 7919) % 50 + 1,
        DATEADD(DAY, CONVERT(int, (CONVERT(bigint, i) - 1) * 3968 / 1000000), CONVERT(date, '20160103')),
        REPLICATE(N'm', 40 + i % 40)
    FROM n;
END
GO

IF NOT EXISTS (SELECT 1 FROM bench.InvoiceLines)
BEGIN
    WITH n AS
    (
        SELECT TOP (10000000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
        FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b CROSS JOIN sys.all_columns AS c
    )
    INSERT bench.InvoiceLines WITH (TABLOCK) (Id, InvoiceId, Amount, Notes)
    SELECT i, (i - 1) / 10 + 1, (i % 1000) + 0.25, REPLICATE(N'n', 20 + i % 30)
    FROM n;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InvoiceLines_InvoiceId' AND object_id = OBJECT_ID(N'bench.InvoiceLines'))
BEGIN
    -- The foreign key's index, covering the measure, as a host would declare for a line table.
    CREATE INDEX IX_InvoiceLines_InvoiceId ON bench.InvoiceLines (InvoiceId) INCLUDE (Amount);
END
GO

-- ---------------------------------------------------------------------------------------------
-- The results, and the procedure that times one shape.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'bench.Results') IS NOT NULL
    DROP TABLE bench.Results;

CREATE TABLE bench.Results
(
    HeaderIndex varchar(20) NOT NULL,
    Since varchar(10) NOT NULL,
    Shape varchar(10) NOT NULL,
    Run int NOT NULL,
    ElapsedMs decimal(12, 3) NOT NULL,
    CpuMs int NOT NULL
);
GO

CREATE OR ALTER PROCEDURE bench.Measure
    @HeaderIndex varchar(20),
    @Since varchar(10),
    @Shape varchar(10),
    @Statement nvarchar(max),
    @Today date,
    @From date
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @parameters nvarchar(200) = N'@today date, @cal varchar(2), @from date';
    DECLARE @sink TABLE (CenterId int, Total decimal(38, 4));
    DECLARE @run int = 0;
    DECLARE @started datetime2(7);
    DECLARE @cpu int;

    -- One run to compile and warm the cache, then seven timed runs of the same cached plan.
    INSERT @sink EXEC sys.sp_executesql @Statement, @parameters, @today = @Today, @cal = 'uq', @from = @From;

    WHILE @run < 7
    BEGIN
        DELETE @sink;
        SELECT @cpu = cpu_time FROM sys.dm_exec_requests WHERE session_id = @@SPID;
        SET @started = SYSDATETIME();

        INSERT @sink EXEC sys.sp_executesql @Statement, @parameters, @today = @Today, @cal = 'uq', @from = @From;

        INSERT bench.Results (HeaderIndex, Since, Shape, Run, ElapsedMs, CpuMs)
        SELECT
            @HeaderIndex,
            @Since,
            @Shape,
            @run,
            DATEDIFF_BIG(MICROSECOND, @started, SYSDATETIME()) / 1000.0,
            r.cpu_time - @cpu
        FROM sys.dm_exec_requests AS r
        WHERE r.session_id = @@SPID;

        SET @run += 1;
    END
END
GO

-- ---------------------------------------------------------------------------------------------
-- The runs: both header indexes, both selectivities, all three shapes.
-- ---------------------------------------------------------------------------------------------
DECLARE @today date = '20261124';
DECLARE @monthStart date =
    (SELECT TOP (1) StartDate FROM bench.CalendarMonths
     WHERE Calendar = 'uq' AND StartDate <= @today AND @today < EndDate ORDER BY StartDate DESC);
DECLARE @yearStart date =
    (SELECT StartDate FROM bench.CalendarMonths
     WHERE Calendar = 'uq' AND Month = 1
       AND Year = (SELECT TOP (1) Year FROM bench.CalendarMonths
                   WHERE Calendar = 'uq' AND StartDate <= @today AND @today < EndDate ORDER BY StartDate DESC));

-- A bound that found no month would make every shape read nothing, and time nothing.
IF @monthStart IS NULL OR @yearStart IS NULL
    THROW 50000, N'The month table does not cover the benchmark''s today.', 1;

PRINT CONCAT(N'Today ', @today, N'; month since ', @monthStart, N'; year since ', @yearStart, N'.');

-- The grouped read, with the lower bound left as a token each shape replaces.
DECLARE @select nvarchar(max) = N'SELECT [P1].[CenterId] AS [c0], SUM([T].[Amount]) AS [c1]
FROM [bench].[InvoiceLines] AS [T]
INNER JOIN [bench].[Invoices] AS [P1] ON [P1].[Id] = [T].[InvoiceId]
WHERE ([P1].[PostingDate] >= {bound})
GROUP BY [P1].[CenterId];';

-- The month lookup, and the year's first month looked up from it, as the engine writes them.
DECLARE @monthLookup nvarchar(max) = N'(SELECT TOP (1) [M].[StartDate] FROM [bench].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @cal AND [M].[StartDate] <= @today AND @today < [M].[EndDate] ORDER BY [M].[StartDate] DESC)';
DECLARE @yearLookup nvarchar(max) = N'(SELECT TOP (1) (SELECT [M2].[StartDate] FROM [bench].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @cal AND [M2].[Year] = [M].[Year] AND [M2].[Month] = 1) FROM [bench].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @cal AND [M].[StartDate] <= @today AND @today < [M].[EndDate] ORDER BY [M].[StartDate] DESC)';

DECLARE @variant int = 0;
WHILE @variant < 2
BEGIN
    DECLARE @headerIndex varchar(20) = CASE @variant WHEN 0 THEN 'covering' ELSE 'non-covering' END;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Invoices_PostingDate' AND object_id = OBJECT_ID(N'bench.Invoices'))
        DROP INDEX IX_Invoices_PostingDate ON bench.Invoices;

    IF @variant = 0
        CREATE INDEX IX_Invoices_PostingDate ON bench.Invoices (PostingDate) INCLUDE (CenterId);
    ELSE
        CREATE INDEX IX_Invoices_PostingDate ON bench.Invoices (PostingDate);

    -- Fresh plans for every variant, so no shape inherits one compiled against the other index.
    DBCC FREEPROCCACHE WITH NO_INFOMSGS;

    DECLARE @since int = 0;
    WHILE @since < 2
    BEGIN
        DECLARE @sinceName varchar(10) = CASE @since WHEN 0 THEN 'month' ELSE 'year' END;
        DECLARE @lookup nvarchar(max) = CASE @since WHEN 0 THEN @monthLookup ELSE @yearLookup END;
        DECLARE @from date = CASE @since WHEN 0 THEN @monthStart ELSE @yearStart END;

        DECLARE @variable nvarchar(max) =
            N'DECLARE @qx0_v0 date = ' + @lookup + N';' + NCHAR(10) + NCHAR(10)
            + REPLACE(@select, N'{bound}', N'@qx0_v0');
        DECLARE @parameter nvarchar(max) = REPLACE(@select, N'{bound}', N'@from');
        DECLARE @scalar nvarchar(max) = REPLACE(@select, N'{bound}', @lookup);
        DECLARE @recompile nvarchar(max) = REPLACE(@variable, N'GROUP BY [P1].[CenterId];', N'GROUP BY [P1].[CenterId] OPTION (RECOMPILE);');

        EXEC bench.Measure @headerIndex, @sinceName, 'variable', @variable, @today, @from;
        EXEC bench.Measure @headerIndex, @sinceName, 'parameter', @parameter, @today, @from;
        EXEC bench.Measure @headerIndex, @sinceName, 'scalar', @scalar, @today, @from;
        EXEC bench.Measure @headerIndex, @sinceName, 'recompile', @recompile, @today, @from;

        SET @since += 1;
    END

    SET @variant += 1;
END
GO

-- ---------------------------------------------------------------------------------------------
-- The summary: median elapsed time per shape, and each shape against the bound parameter.
-- ---------------------------------------------------------------------------------------------
WITH medians AS
(
    SELECT DISTINCT
        HeaderIndex,
        Since,
        Shape,
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ElapsedMs) OVER (PARTITION BY HeaderIndex, Since, Shape) AS MedianMs,
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CpuMs) OVER (PARTITION BY HeaderIndex, Since, Shape) AS MedianCpuMs
    FROM bench.Results
)
SELECT
    m.HeaderIndex,
    m.Since,
    m.Shape,
    CONVERT(decimal(12, 1), m.MedianMs) AS MedianMs,
    CONVERT(decimal(12, 0), m.MedianCpuMs) AS MedianCpuMs,
    CONVERT(decimal(6, 2), m.MedianMs / NULLIF(p.MedianMs, 0)) AS VersusParameter
FROM medians AS m
INNER JOIN medians AS p ON p.HeaderIndex = m.HeaderIndex AND p.Since = m.Since AND p.Shape = 'parameter'
ORDER BY m.HeaderIndex, m.Since, m.Shape;
GO
