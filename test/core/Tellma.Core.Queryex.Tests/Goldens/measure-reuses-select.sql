SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Amount]) AS [c1],
    MAX([T].[PostingDate]) AS [c2],
    MIN([T].[PostingDate]) AS [c3]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull SUM(amount)
--   2 : QxDate NotNull max(PostingDate)
--   3 : QxDate NotNull min(PostingDate)
-- partials
--   1 : Sum QxNumeric
--   2 : Max QxDate
--   3 : Min QxDate
-- measures
--   QxNumeric Nullable sum(Amount) * 2
--   QxDate Nullable max(PostingDate)
--   QxDate Nullable min(PostingDate)
