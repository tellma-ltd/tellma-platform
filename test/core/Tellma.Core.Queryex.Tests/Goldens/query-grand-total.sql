SELECT
    COUNT_BIG(*) AS [c0],
    SUM([T].[Amount]) AS [c1]
FROM [gl].[Documents] AS [T]
;

-- parameters
-- columns
--   0 : QxNumeric NotNull count()
--   1 : QxNumeric Nullable sum(Amount)
