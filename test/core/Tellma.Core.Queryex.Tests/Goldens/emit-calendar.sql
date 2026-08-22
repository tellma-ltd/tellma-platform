SELECT
    DATEPART(YEAR, [T].[PostingDate]) AS [c0],
    DATEPART(QUARTER, [T].[PostingDate]) AS [c1],
    DATEPART(MONTH, [T].[PostingDate]) AS [c2],
    DATEPART(DAY, [T].[PostingDate]) AS [c3],
    DATEPART(ISO_WEEK, [T].[PostingDate]) AS [c4],
    ((DATEDIFF(DAY, CONVERT(date, '00010101'), [T].[PostingDate]) % 7) + 1) AS [c5],
    DATEADD(DAY, (DATEDIFF(DAY, CONVERT(date, '00010101'), [T].[PostingDate]) / 7) * 7, CONVERT(date, '00010101')) AS [c6],
    DATEADD(MONTH, DATEDIFF(MONTH, CONVERT(date, '00010101'), [T].[PostingDate]), CONVERT(date, '00010101')) AS [c7],
    DATEADD(YEAR, DATEDIFF(YEAR, CONVERT(date, '00010101'), [T].[PostingDate]), CONVERT(date, '00010101')) AS [c8],
    CAST([T].[PostedOn] AS date) AS [c9]
FROM [gl].[Documents] AS [T]
;

-- parameters
-- columns
--   0 : QxNumeric NotNull year(PostingDate)
--   1 : QxNumeric NotNull quarter(PostingDate)
--   2 : QxNumeric NotNull month(PostingDate)
--   3 : QxNumeric NotNull day(PostingDate)
--   4 : QxNumeric NotNull week(PostingDate)
--   5 : QxNumeric NotNull weekday(PostingDate)
--   6 : QxDate NotNull startOfWeek(PostingDate)
--   7 : QxDate NotNull startOfMonth(PostingDate)
--   8 : QxDate NotNull startOfYear(PostingDate)
--   9 : QxDate NotNull startOfDay(PostedOn)
