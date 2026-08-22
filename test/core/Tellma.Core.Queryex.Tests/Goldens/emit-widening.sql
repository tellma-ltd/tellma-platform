SELECT
    SUM(CAST([T].[LineCount] AS decimal(38, 0))) AS [c0],
    AVG(CAST([T].[LineCount] AS decimal(19, 0))) AS [c1],
    SUM([T].[Amount]) AS [c2],
    AVG([T].[Amount]) AS [c3],
    (CAST([T].[LineCount] AS decimal(19, 0)) / [T].[CreatedById]) AS [c4],
    ([T].[Amount] / [T].[Rate]) AS [c5]
FROM [gl].[Documents] AS [T]
GROUP BY (CAST([T].[LineCount] AS decimal(19, 0)) / [T].[CreatedById]), ([T].[Amount] / [T].[Rate])
;

-- parameters
-- columns
--   0 : QxNumeric NotNull sum(Count)
--   1 : QxNumeric NotNull avg(Count)
--   2 : QxNumeric NotNull sum(Amount)
--   3 : QxNumeric NotNull avg(Amount)
--   4 : QxNumeric NotNull key Count / CreatedById
--   5 : QxNumeric Nullable key Amount / Rate
