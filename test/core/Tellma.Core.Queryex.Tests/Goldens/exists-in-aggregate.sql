SELECT
    [P1].[SegmentName] AS [c0],
    COUNT_BIG(*) AS [c1],
    SUM([T].[Amount]) AS [c2]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE ([X1].[DocumentId] = [T].[DocumentId]))
GROUP BY [P1].[SegmentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull count()
--   2 : QxNumeric NotNull sum(Amount)
