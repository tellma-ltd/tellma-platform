SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Amount]) AS [c1]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
HAVING (COUNT_BIG(CASE WHEN (1 = 1) THEN [T].[DocumentId] END) > @qx0_p0)
ORDER BY [c0] ASC
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull sum(Amount)
