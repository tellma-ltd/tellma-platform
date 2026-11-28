SELECT
    [C1].[Year] AS [c0],
    (([C1].[Month] + 2) / 3) AS [c1],
    [C1].[Month] AS [c2],
    (DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate]) + 1) AS [c3],
    [C1].[StartDate] AS [c4]
FROM [gl].[Documents] AS [T]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [T].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [T].[PostingDate] < [M].[EndDate]) AS [C1]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxNumeric Nullable year(PostingDate, 'uq')
--   1 : QxNumeric Nullable quarter(PostingDate, 'uq')
--   2 : QxNumeric Nullable month(PostingDate, 'uq')
--   3 : QxNumeric Nullable day(PostingDate, 'uq')
--   4 : QxDate Nullable startOfMonth(PostingDate, 'uq')
