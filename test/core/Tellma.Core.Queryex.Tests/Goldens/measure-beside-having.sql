SELECT
    [P1].[SegmentName] AS [c0],
    COUNT_BIG(*) AS [c1],
    MAX([T].[Amount]) AS [c2],
    MIN([T].[Amount]) AS [c3]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
HAVING (SUM([T].[Amount]) > (-@qx0_p0))
ORDER BY [c0] ASC
OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(4, 0) Literal = 1000
--   @qx0_take : QxNumeric QxInt Literal = 10
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull count()
--   2 : QxNumeric NotNull max(Amount)
--   3 : QxNumeric NotNull min(Amount)
-- partials
--   1 : Count QxNumeric
--   2 : Max QxNumeric
--   3 : Min QxNumeric
-- measures
--   QxNumeric NotNull count()
--   QxNumeric Nullable max(Amount) - min(Amount)
