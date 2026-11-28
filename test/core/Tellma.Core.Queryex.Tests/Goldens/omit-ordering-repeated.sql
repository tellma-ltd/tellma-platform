SELECT
    [P1].[SegmentName] AS [c0],
    SUM(CASE WHEN (1 = 1) THEN [T].[Amount] END) AS [c1],
    SUM(CASE WHEN (1 = 1) THEN [T].[Amount] END) AS [c2]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
ORDER BY [c1] DESC, [c0] ASC
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric Nullable sum(Amount, CustomerId = @Customer)
--   2 : QxNumeric Nullable sum(Amount, CustomerId = @Other)
