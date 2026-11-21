SELECT
    [P1].[SegmentName] AS [c0],
    SUM(CASE WHEN (1 = 1) THEN [T].[Amount] END) AS [c1],
    SUM([T].[Amount]) AS [c2],
    COUNT_BIG(CASE WHEN ((@qx0_p0 IS NOT NULL) AND ([T].[PostingDate] <= @qx0_p0)) THEN [T].[Amount] END) AS [c3]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
--   @qx0_p0 : QxDate QxDate Declared <- AsOf
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric Nullable sum(Amount, CustomerId = @Customer)
--   2 : QxNumeric NotNull sum(Amount)
--   3 : QxNumeric NotNull count(Amount, PostingDate <= @AsOf)
-- partials
--   1 : Sum QxNumeric
--   2 : Sum QxNumeric
--   3 : Count QxNumeric
-- measures
--   QxNumeric Nullable sum(Amount, CustomerId = @Customer)
--   QxNumeric NotNull sum(Amount)
--   QxNumeric NotNull count(Amount, PostingDate <= @AsOf)
