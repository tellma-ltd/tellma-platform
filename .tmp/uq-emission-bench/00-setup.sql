-- 'uq' emission benchmark: Umm al-Qura month table (spec 0013 §8.2's shape) and a 10M-row fact table.
-- Run against master; recreates UqBench from scratch.

IF DB_ID('UqBench') IS NOT NULL
BEGIN
    ALTER DATABASE UqBench SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE UqBench;
END
GO
CREATE DATABASE UqBench;
GO
ALTER DATABASE UqBench SET RECOVERY SIMPLE;
ALTER DATABASE UqBench SET QUERY_STORE = ON;
ALTER DATABASE UqBench SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, INTERVAL_LENGTH_MINUTES = 1);
GO
USE UqBench;
GO
ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 4;
GO

-- §F.13's month table with the host obligations' two unique indexes.
CREATE TABLE dbo.CalendarMonths (
    Calendar  varchar(10) NOT NULL,
    [Year]    int         NOT NULL,
    [Month]   int         NOT NULL,
    StartDate date        NOT NULL,
    EndDate   date        NOT NULL,
    CONSTRAINT PK_CalendarMonths PRIMARY KEY CLUSTERED (Calendar, StartDate)
);
CREATE UNIQUE INDEX UX_CalendarMonths_YearMonth ON dbo.CalendarMonths (Calendar, [Year], [Month]);
GO
:r calendar-months.sql
GO

-- Facts: 10M lines, uniform over 2016-01-01 .. 2025-06-15 ("today"), 50K customers.
CREATE TABLE dbo.Lines (
    Id          bigint        NOT NULL CONSTRAINT PK_Lines PRIMARY KEY CLUSTERED,
    CustomerId  int           NOT NULL,
    PostingDate date          NOT NULL,
    Amount      decimal(19,4) NOT NULL,
    Memo        nvarchar(100) NULL
);
WITH d AS (SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)),
     g AS (SELECT CAST(1 + a.n + 10 * b.n + 100 * c.n + 1000 * e.n + 10000 * f.n + 100000 * h.n + 1000000 * i.n AS bigint) AS Id
           FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e CROSS JOIN d f CROSS JOIN d h CROSS JOIN d i)
INSERT INTO dbo.Lines WITH (TABLOCK) (Id, CustomerId, PostingDate, Amount, Memo)
SELECT Id,
       CAST((Id * 48271) % 2147483647 % 50000 AS int) + 1,
       DATEADD(DAY, CAST((Id * 16807) % 2147483647 % 3454 AS int), CAST('2016-01-01' AS date)),
       CAST((Id * 69621) % 2147483647 % 10000000 AS decimal(19,4)) / 100,
       N'Line memo text ' + CAST(Id AS nvarchar(20))
FROM g
OPTION (MAXDOP 8);
GO
UPDATE STATISTICS dbo.Lines WITH FULLSCAN;
UPDATE STATISTICS dbo.CalendarMonths WITH FULLSCAN;
GO
