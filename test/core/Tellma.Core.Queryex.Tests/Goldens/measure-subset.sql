SELECT
    [P1].[SegmentName] AS [c0],
    SUM([T].[Amount]) AS [c1],
    COUNT_BIG(*) AS [c2],
    MAX([T].[Rate]) AS [c3],
    MAX([T].[PostingDate]) AS [c4],
    MIN([T].[PostingDate]) AS [c5],
    MAX([T].[DueDate]) AS [c6],
    MIN([T].[Amount]) AS [c7],
    MIN([T].[LineCount]) AS [c8],
    MAX([T].[DocCode]) AS [c9],
    MIN([T].[Memo]) AS [c10],
    CAST(MAX(CAST([T].[IsPosted] AS tinyint)) AS bit) AS [c11]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull sum(Amount)
--   2 : QxNumeric NotNull count()
--   3 : QxNumeric Nullable max(Rate)
--   4 : QxDate NotNull max(PostingDate)
--   5 : QxDate NotNull min(PostingDate)
--   6 : QxDate Nullable max(DueDate)
--   7 : QxNumeric NotNull min(Amount)
--   8 : QxNumeric NotNull min(Count)
--   9 : QxString NotNull max(Code)
--   10 : QxString Nullable min(Memo)
--   11 : QxBool NotNull max(IsPosted)
-- partials
--   1 : Sum QxNumeric
--   2 : Count QxNumeric
--   3 : Max QxNumeric
--   4 : Max QxDate
--   5 : Min QxDate
--   6 : Max QxDate
--   7 : Min QxNumeric
--   8 : Min QxNumeric
--   9 : Max QxString
--   10 : Min QxString
--   11 : Max QxBool
-- measures
--   QxNumeric NotNull if(sum(Amount) > 0 and count() in (1, 2, 3), round(sum(Amount) / count(), 2), coalesce(max(Rate), -1))
--   QxNumeric NotNull year(max(PostingDate)) * 100 + month(max(PostingDate), 'gc')
--   QxNumeric Nullable diffDays(min(PostingDate), max(DueDate))
--   QxNumeric NotNull cast(abs(min(Amount)), 'numeric')
--   QxDate Nullable coalesce(max(DueDate), cast(null, 'date'))
--   QxNumeric NotNull floor(sum(Amount)) + ceiling(min(Count) * 1.5)
--   QxString NotNull max(Code)
--   QxString Nullable min(Memo)
--   QxBool NotNull max(IsPosted)
