SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Amount]) AS [c1],
    COUNT_BIG(*) AS [c2],
    AVG(CAST([T].[LineCount] AS decimal(19, 0))) AS [c3],
    MIN([T].[PostingDate]) AS [c4],
    CAST(MAX(CAST([T].[IsPosted] AS tinyint)) AS bit) AS [c5]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
HAVING (SUM([T].[Amount]) > @qx0_p0)
ORDER BY [c1] DESC
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(3, 0) Literal = 100
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull sum(Amount)
--   2 : QxNumeric NotNull count()
--   3 : QxNumeric NotNull avg(Count)
--   4 : QxDate NotNull min(PostingDate)
--   5 : QxBool NotNull max(IsPosted)
