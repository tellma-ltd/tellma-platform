SELECT
    [P1].[SegmentName] AS [c0],
    SUM(CASE WHEN ([T].[IsPosted] = 1) THEN [T].[Amount] END) AS [c1],
    SUM(CASE WHEN NOT (([T].[IsPosted] = 1)) THEN [T].[Amount] END) AS [c2],
    SUM(CASE WHEN ([T].[IsPosted] = 1) THEN [T].[Rate] END) AS [c3],
    COUNT_BIG(CASE WHEN ([T].[IsPosted] = 1) THEN [T].[Rate] END) AS [c4]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric Nullable sum(Amount, IsPosted)
--   2 : QxNumeric Nullable sum(Amount, not IsPosted)
--   3 : QxNumeric Nullable sum(Rate, IsPosted)
--   4 : QxNumeric NotNull count(Rate, IsPosted)
-- partials
--   1 : Sum QxNumeric
--   2 : Sum QxNumeric
--   3 : Sum QxNumeric
--   4 : Count QxNumeric
-- measures
--   QxNumeric Nullable sum(Amount, IsPosted) - sum(Amount, not IsPosted)
--   QxNumeric Nullable avg(Rate, IsPosted)
--   QxNumeric NotNull count(Rate, IsPosted)
