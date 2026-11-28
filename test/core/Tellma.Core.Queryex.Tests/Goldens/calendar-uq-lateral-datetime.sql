SELECT
    [C1].[Month] AS [c0],
    DATEADD(DAY, DATEDIFF(DAY, [C1].[StartDate], [C2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [C2].[StartDate], [C2].[EndDate]) - 1 - DATEDIFF(DAY, [C1].[StartDate], [B1].[v])), [T].[PostedOn]) AS [c1],
    (DATEDIFF(DAY, [C3].[StartDate], [B3].[v]) + 1) AS [c2]
FROM [gl].[Documents] AS [T]
CROSS APPLY (VALUES (CAST([T].[PostedOn] AS date))) AS [B1] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [B1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [B1].[v] < [M].[EndDate]) AS [C1]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST(@qx0_p1 AS int)) / 12) AND [M].[Month] = (([C1].[Year] * 12 + [C1].[Month] - 1 + CAST(@qx0_p1 AS int)) % 12 + 1)) AS [C2]
CROSS APPLY (VALUES (DATEADD(DAY, @qx0_p1, [T].[PostedOn]))) AS [B2] ([v])
CROSS APPLY (VALUES (CAST([B2].[v] AS date))) AS [B3] ([v])
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [B3].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [B3].[v] < [M].[EndDate]) AS [C3]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxNumeric Nullable month(PostedOn, 'uq')
--   1 : QxDateTime Nullable addMonths(PostedOn, 1, 'uq')
--   2 : QxNumeric Nullable day(addDays(PostedOn, 1), 'uq')
