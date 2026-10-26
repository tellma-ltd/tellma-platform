-- Tree-rollup benchmark: database, tables and data generators.
-- Shapes follow spec 0012's tree convention: a hierarchyid Node of the id path with a unique index.
-- Run against master; recreates TreeRollupBench from scratch.

IF DB_ID('TreeRollupBench') IS NOT NULL
BEGIN
    ALTER DATABASE TreeRollupBench SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE TreeRollupBench;
END
GO
CREATE DATABASE TreeRollupBench;
GO
ALTER DATABASE TreeRollupBench SET RECOVERY SIMPLE;
ALTER DATABASE TreeRollupBench SET QUERY_STORE = ON;
ALTER DATABASE TreeRollupBench SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL,
    INTERVAL_LENGTH_MINUTES = 1, MAX_STORAGE_SIZE_MB = 2048);
GO
USE TreeRollupBench;
GO
-- One database's share of an elastic pool: four schedulers.
ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 4;
GO

-- Numbers 1..1,000,000 for set-based generation.
CREATE TABLE dbo.Nums (n int NOT NULL PRIMARY KEY CLUSTERED);
WITH d AS (SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n))
INSERT INTO dbo.Nums WITH (TABLOCK) (n)
SELECT 1 + a.n + 10 * b.n + 100 * c.n + 1000 * e.n + 10000 * f.n + 100000 * g.n
FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e CROSS JOIN d f CROSS JOIN d g;
GO

-- The two tree tables share one shape: spec 0012's TreeEntity (Node + UX_<Table>_Node + IX_<Table>_ParentId).
CREATE TABLE dbo.Centers (
    Id       int           NOT NULL CONSTRAINT PK_Centers PRIMARY KEY CLUSTERED,
    ParentId int           NULL,
    Node     hierarchyid   NOT NULL,
    Name     nvarchar(255) NOT NULL,
    Name2    nvarchar(255) NULL,
    Code     varchar(50)   NOT NULL
);
CREATE UNIQUE INDEX UX_Centers_Node ON dbo.Centers (Node);
CREATE INDEX IX_Centers_ParentId ON dbo.Centers (ParentId);

CREATE TABLE dbo.Accounts (
    Id       int           NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY CLUSTERED,
    ParentId int           NULL,
    Node     hierarchyid   NOT NULL,
    Name     nvarchar(255) NOT NULL,
    Name2    nvarchar(255) NULL,
    Code     varchar(50)   NOT NULL
);
CREATE UNIQUE INDEX UX_Accounts_Node ON dbo.Accounts (Node);
CREATE INDEX IX_Accounts_ParentId ON dbo.Accounts (ParentId);

-- Materialized closures, for the C-mat variant only.
CREATE TABLE dbo.CentersClosure (
    DescendantId int NOT NULL,
    AncestorId   int NOT NULL,
    Distance     int NOT NULL,
    CONSTRAINT PK_CentersClosure PRIMARY KEY CLUSTERED (DescendantId, AncestorId)
);
CREATE TABLE dbo.AccountsClosure (
    DescendantId int NOT NULL,
    AncestorId   int NOT NULL,
    Distance     int NOT NULL,
    CONSTRAINT PK_AccountsClosure PRIMARY KEY CLUSTERED (DescendantId, AncestorId)
);

-- Leaves numbered 1..L, so facts can pick a leaf by hash.
CREATE TABLE dbo.CenterLeaves (rn int NOT NULL PRIMARY KEY CLUSTERED, Id int NOT NULL);
CREATE TABLE dbo.AccountLeaves (rn int NOT NULL PRIMARY KEY CLUSTERED, Id int NOT NULL);

CREATE TABLE dbo.TreeStage (
    Id       int          NOT NULL PRIMARY KEY CLUSTERED,
    ParentId int          NULL,
    Depth    int          NOT NULL,
    Path     varchar(400) NOT NULL
);
GO

-- Builds a tree level by level: @Branch roots, @Branch children per node. Below level 1 a node
-- becomes a leaf with probability @LeafPct% (0 = a uniform tree with every leaf at @Depth).
CREATE OR ALTER PROCEDURE dbo.BuildTree @Target sysname, @Depth int, @Branch int, @LeafPct int, @IdBase int
AS
BEGIN
    SET NOCOUNT ON;
    TRUNCATE TABLE dbo.TreeStage;

    -- Roots.
    INSERT INTO dbo.TreeStage (Id, ParentId, Depth, Path)
    SELECT @IdBase + n, NULL, 1, '/' + CAST(@IdBase + n AS varchar(11)) + '/'
    FROM dbo.Nums WHERE n <= @Branch;

    -- Each further level: every non-leaf node of the previous level gets @Branch children.
    DECLARE @d int = 2;
    WHILE @d <= @Depth
    BEGIN
        DECLARE @maxId int = (SELECT MAX(Id) FROM dbo.TreeStage);
        INSERT INTO dbo.TreeStage (Id, ParentId, Depth, Path)
        SELECT @maxId + ROW_NUMBER() OVER (ORDER BY p.Id, c.n), p.Id, @d,
               p.Path + CAST(@maxId + ROW_NUMBER() OVER (ORDER BY p.Id, c.n) AS varchar(11)) + '/'
        FROM dbo.TreeStage AS p
        CROSS JOIN dbo.Nums AS c
        WHERE p.Depth = @d - 1 AND c.n <= @Branch
          AND (p.Depth = 1 OR ABS(CHECKSUM(p.Id, 31)) % 100 >= @LeafPct);
        SET @d += 1;
    END;

    -- Copy into the target tree, its leaves and its closure.
    DECLARE @sql nvarchar(max) = N'
        TRUNCATE TABLE dbo.<T>;
        INSERT INTO dbo.<T> (Id, ParentId, Node, Name, Name2, Code)
        SELECT Id, ParentId, hierarchyid::Parse(Path), N''Member '' + CAST(Id AS nvarchar(11)),
               N''عضو '' + CAST(Id AS nvarchar(11)), ''C'' + CAST(Id AS varchar(11))
        FROM dbo.TreeStage;
        TRUNCATE TABLE dbo.<L>;
        INSERT INTO dbo.<L> (rn, Id)
        SELECT ROW_NUMBER() OVER (ORDER BY s.Id), s.Id FROM dbo.TreeStage AS s
        WHERE NOT EXISTS (SELECT 1 FROM dbo.TreeStage AS c WHERE c.ParentId = s.Id);
        TRUNCATE TABLE dbo.<T>Closure;
        INSERT INTO dbo.<T>Closure (DescendantId, AncestorId, Distance)
        SELECT d.Id, a.Id, k.n - 1
        FROM dbo.<T> AS d
        JOIN dbo.Nums AS k ON k.n <= d.Node.GetLevel()
        JOIN dbo.<T> AS a ON a.Node = d.Node.GetAncestor(k.n - 1);
        UPDATE STATISTICS dbo.<T> WITH FULLSCAN;
        UPDATE STATISTICS dbo.<T>Closure WITH FULLSCAN;';
    SET @sql = REPLACE(REPLACE(@sql, N'<T>', @Target),
                       N'<L>', CASE @Target WHEN N'Centers' THEN N'CenterLeaves' ELSE N'AccountLeaves' END);
    EXEC sys.sp_executesql @sql;
END;
GO

-- Builds @N fact lines on random leaves of both trees; @NullPct% have no center. One year of dates.
CREATE OR ALTER PROCEDURE dbo.BuildFacts @N int, @NullPct int
AS
BEGIN
    SET NOCOUNT ON;
    DROP TABLE IF EXISTS dbo.Lines;
    CREATE TABLE dbo.Lines (
        Id          bigint        NOT NULL CONSTRAINT PK_Lines PRIMARY KEY CLUSTERED,
        CenterId    int           NULL,
        AccountId   int           NOT NULL,
        PostingDate date          NOT NULL,
        Amount      decimal(19,4) NOT NULL,
        Quantity    decimal(19,4) NOT NULL,
        Memo        nvarchar(100) NULL
    );
    DECLARE @CL int = (SELECT COUNT(*) FROM dbo.CenterLeaves), @AL int = (SELECT COUNT(*) FROM dbo.AccountLeaves);

    -- Pseudo-random columns: Lehmer-style multiplicative hashes of the id modulo a prime, one
    -- multiplier per column (CHECKSUM of sequential ids clusters badly).
    INSERT INTO dbo.Lines WITH (TABLOCK) (Id, CenterId, AccountId, PostingDate, Amount, Quantity, Memo)
    SELECT g.Id,
           CASE WHEN g.h3 % 100 < @NullPct THEN NULL ELSE cl.Id END,
           al.Id,
           DATEADD(DAY, CAST(g.h4 % 365 AS int), CAST('2025-01-01' AS date)),
           CAST(g.h5 % 10000000 AS decimal(19,4)) / 100,
           CAST(g.h5 % 1000 AS decimal(19,4)),
           N'Line memo text ' + CAST(g.Id AS nvarchar(20))
    FROM (SELECT x.Id,
                 (x.Id * 16807) % 2147483647 AS h1,
                 (x.Id * 48271) % 2147483647 AS h2,
                 (x.Id * 69621) % 2147483647 AS h3,
                 (x.Id * 39373) % 2147483647 AS h4,
                 (x.Id * 1583458089) % 2147483647 AS h5
          FROM (SELECT CAST(a.n AS bigint) + 1000000 * b.k AS Id
                FROM dbo.Nums AS a
                CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),(17),(18),(19)) AS b(k)
                WHERE CAST(a.n AS bigint) + 1000000 * b.k <= @N) AS x) AS g
    JOIN dbo.CenterLeaves AS cl ON cl.rn = CAST(g.h1 % @CL AS int) + 1
    JOIN dbo.AccountLeaves AS al ON al.rn = CAST(g.h2 % @AL AS int) + 1
    OPTION (MAXDOP 8);

    UPDATE STATISTICS dbo.Lines WITH FULLSCAN;
END;
GO
