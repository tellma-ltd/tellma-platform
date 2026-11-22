SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Amount]) AS [c1],
    COUNT_BIG(*) AS [c2],
    COUNT_BIG([T].[Amount]) AS [c3]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull sum(Amount)
--   2 : QxNumeric NotNull count()
--   3 : QxNumeric NotNull count(Amount)
-- partials
--   1 : Sum QxNumeric
--   2 : Count QxNumeric
--   3 : Count QxNumeric
-- measures
--   QxNumeric Nullable sum(Amount)
--   QxNumeric Nullable sum(Amount) / count()
--   QxNumeric Nullable avg(Amount)
