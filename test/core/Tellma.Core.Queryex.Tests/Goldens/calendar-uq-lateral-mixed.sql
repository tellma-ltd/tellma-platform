SELECT
    ((([C2].[Year] * 12 + [C2].[Month]) - ([C1].[Year] * 12 + [C1].[Month])) + CASE WHEN (([C2].[Year] * 12 + [C2].[Month]) - ([C1].[Year] * 12 + [C1].[Month])) > 0 AND DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C2].[StartDate], [C2].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate])), [T].[PostingDate]) > @qx0_p1 THEN -1 WHEN (([C2].[Year] * 12 + [C2].[Month]) - ([C1].[Year] * 12 + [C1].[Month])) < 0 AND DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C2].[StartDate], [C2].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [T].[PostingDate])), [T].[PostingDate]) < @qx0_p1 THEN 1 ELSE 0 END) AS [c0],
    (([C4].[Year] - [C3].[Year]) + CASE WHEN ([C4].[Year] - [C3].[Year]) > 0 AND DATEADD(DAY, DATEDIFF(DAY, [C3].[StartDate], [C5].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C5].[StartDate], [C5].[EndDate]) - 1 - DATEDIFF(DAY, [C3].[StartDate], [T].[DueDate])), [T].[DueDate]) > [B1].[v] THEN -1 WHEN ([C4].[Year] - [C3].[Year]) < 0 AND DATEADD(DAY, DATEDIFF(DAY, [C3].[StartDate], [C5].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C5].[StartDate], [C5].[EndDate]) - 1 - DATEDIFF(DAY, [C3].[StartDate], [T].[DueDate])), [T].[DueDate]) < [B1].[v] THEN 1 ELSE 0 END) AS [c1],
    ((([C7].[Year] * 12 + [C7].[Month]) - ([C6].[Year] * 12 + [C6].[Month])) + CASE WHEN (([C7].[Year] * 12 + [C7].[Month]) - ([C6].[Year] * 12 + [C6].[Month])) > 0 AND DATEADD(DAY, DATEDIFF(DAY, [C6].[StartDate], [C7].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C7].[StartDate], [C7].[EndDate]) - 1 - DATEDIFF(DAY, [C6].[StartDate], [B2].[v])), [T].[PostedOn]) > [T].[DueOn] THEN -1 WHEN (([C7].[Year] * 12 + [C7].[Month]) - ([C6].[Year] * 12 + [C6].[Month])) < 0 AND DATEADD(DAY, DATEDIFF(DAY, [C6].[StartDate], [C7].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C7].[StartDate], [C7].[EndDate]) - 1 - DATEDIFF(DAY, [C6].[StartDate], [B2].[v])), [T].[PostedOn]) < [T].[DueOn] THEN 1 ELSE 0 END) AS [c2]
FROM [gl].[Documents] AS [T]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [T].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [T].[PostingDate] < [M].[EndDate]) AS [C1]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= @qx0_p1 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p1 < [M].[EndDate]) AS [C2]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [T].[DueDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [T].[DueDate] < [M].[EndDate]) AS [C3]
CROSS APPLY (VALUES (DATEADD(YEAR, @qx0_p2, [T].[PostingDate]))) AS [B1] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [B1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [B1].[v] < [M].[EndDate]) AS [C4]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = [C4].[Year] AND [M].[Month] = [C3].[Month]) AS [C5]
CROSS APPLY (VALUES (CAST([T].[PostedOn] AS date))) AS [B2] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [B2].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [B2].[v] < [M].[EndDate]) AS [C6]
CROSS APPLY (VALUES (CAST([T].[DueOn] AS date))) AS [B3] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [B3].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [B3].[v] < [M].[EndDate]) AS [C7]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p1 : QxDate QxDate Today
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxNumeric Nullable diffMonths(PostingDate, today(), 'uq')
--   1 : QxNumeric Nullable diffYears(DueDate, addYears(PostingDate, 2), 'uq')
--   2 : QxNumeric Nullable diffMonths(PostedOn, DueOn, 'uq')
