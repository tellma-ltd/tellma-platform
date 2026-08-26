SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Rate]) AS [c1]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
HAVING (EXISTS (SELECT SUM([T].[Rate]) INTERSECT SELECT @qx0_p0) AND (CASE WHEN (SUM([T].[Rate]) > @qx0_p1) THEN 1 ELSE 0 END = 1) AND NOT EXISTS (SELECT SUM([T].[Rate]) INTERSECT SELECT @qx0_p2))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(3, 2) Literal = 3.75
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric Nullable sum(Rate)
