SELECT
    [C2].[StartDate] AS [c0],
    (DATEDIFF(DAY, [C2].[StartDate], [T].[PostingDate]) / 7 + 1) AS [c1],
    [C3].[StartDate] AS [c2],
    DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C4].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C4].[StartDate], [C4].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate])), [T].[PostingDate]) AS [c3],
    DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C5].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C5].[StartDate], [C5].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate])), [T].[PostingDate]) AS [c4],
    DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C6].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C6].[StartDate], [C6].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate])), [T].[PostingDate]) AS [c5]
FROM [gl].[Documents] AS [T]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [T].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [T].[PostingDate] < [M].[EndDate]) AS [C1]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = [C1].[Year] AND [M].[Month] = 1) AS [C2]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = [C1].[Year] AND [M].[Month] = ((([C1].[Month] - 1) / 3) * 3 + 1)) AS [C3]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST([T].[LineCount] AS int)) / 12) AND [M].[Month] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST([T].[LineCount] AS int)) % 12 + 1)) AS [C4]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = ([C1].[Year] + CAST(@qx0_p1 AS int)) AND [M].[Month] = [C1].[Month]) AS [C5]
CROSS APPLY (VALUES ((-@qx0_p2))) AS [B1] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST([B1].[v] AS int)) / 12) AND [M].[Month] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST([B1].[v] AS int)) % 12 + 1)) AS [C6]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p2 : QxNumeric QxDecimal(2, 1) Literal = 1.5
-- columns
--   0 : QxDate Nullable startOfYear(PostingDate, 'uq')
--   1 : QxNumeric Nullable week(PostingDate, 'uq')
--   2 : QxDate Nullable startOfQuarter(PostingDate, 'uq')
--   3 : QxDate Nullable addMonths(PostingDate, Count, 'uq')
--   4 : QxDate Nullable addYears(PostingDate, 1, 'uq')
--   5 : QxDate Nullable addMonths(PostingDate, -1.5, 'uq')
