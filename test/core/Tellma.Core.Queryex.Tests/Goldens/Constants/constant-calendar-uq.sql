SELECT
    (SELECT [M].[Month] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= @qx2_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx2_p0 < [M].[EndDate]) AS [c0],
    (SELECT (SELECT [M2].[StartDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx2_p1 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = ((([M].[Month] - 1) / 3) * 3 + 1)) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= @qx2_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx2_p0 < [M].[EndDate]) AS [c1],
    (SELECT (SELECT (DATEDIFF(DAY, [M2].[StartDate], @qx2_p0) / 7 + 1) FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx2_p1 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = 1) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= @qx2_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx2_p0 < [M].[EndDate]) AS [c2],
    (SELECT (SELECT ((([M2].[Year] * 12 + [M2].[Month]) - ([M].[Year] * 12 + [M].[Month])) + CASE WHEN DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], @qx2_p2)), @qx2_p2) IS NULL THEN NULL WHEN (([M2].[Year] * 12 + [M2].[Month]) - ([M].[Year] * 12 + [M].[Month])) > 0 AND DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], @qx2_p2)), @qx2_p2) > @qx2_p0 THEN -1 WHEN (([M2].[Year] * 12 + [M2].[Month]) - ([M].[Year] * 12 + [M].[Month])) < 0 AND DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], @qx2_p2)), @qx2_p2) < @qx2_p0 THEN 1 ELSE 0 END) FROM (SELECT TOP (1) [M2].[Year], [M2].[Month], [M2].[StartDate], [M2].[EndDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx2_p1 AND [M2].[StartDate] <= @qx2_p0 ORDER BY [M2].[StartDate] DESC) AS [M2] WHERE @qx2_p0 < [M2].[EndDate]) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= @qx2_p2 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx2_p2 < [M].[EndDate]) AS [c3],
    (SELECT (SELECT DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], [V2].[v])), [V1].[v]) FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx2_p1 AND [M2].[Year] = (([M].[Year] * 12 + [M].[Month] - 1 + CAST(@qx2_p5 AS int)) / 12) AND [M2].[Month] = (([M].[Year] * 12 + [M].[Month] - 1 + CAST(@qx2_p5 AS int)) % 12 + 1)) FROM (VALUES (CAST(@qx2_p3 AT TIME ZONE @qx2_p4 AS datetime2(7)))) AS [V1] ([v]) CROSS APPLY (VALUES (CAST([V1].[v] AS date))) AS [V2] ([v]) CROSS APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= [V2].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [V2].[v] < [M].[EndDate]) AS [c4],
    (SELECT [M].[Year] FROM (VALUES (CONVERT(date, @qx2_p6, 23))) AS [V1] ([v]) CROSS APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= [V1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [V1].[v] < [M].[EndDate]) AS [c5],
    (SELECT (SELECT DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], [V1].[v])), [V1].[v]) FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx2_p1 AND [M2].[Year] = ([M].[Year] + CAST(@qx2_p5 AS int)) AND [M2].[Month] = [M].[Month]) FROM (VALUES (CONVERT(date, @qx2_p7, 23))) AS [V1] ([v]) CROSS APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= [V1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [V1].[v] < [M].[EndDate]) AS [c6]
;

-- parameters
--   @qx2_p0 : QxDate QxDate Today
--   @qx2_p1 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx2_p2 : QxDate QxDate Literal = 01/01/2024
--   @qx2_p3 : QxDateTimeOffset QxDateTimeOffset(7) Now
--   @qx2_p4 : QxString QxNVarChar(4000) TimeZone
--   @qx2_p5 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx2_p6 : QxString QxNVarChar(4000) Literal = '1900-04-29'
--   @qx2_p7 : QxString QxNVarChar(4000) Literal = '2077-11-01'
-- columns
--   0 : QxNumeric Nullable month(today(), 'uq')
--   1 : QxDate Nullable startOfQuarter(today(), 'uq')
--   2 : QxNumeric Nullable week(today(), 'uq')
--   3 : QxNumeric Nullable diffMonths('2024-01-01', today(), 'uq')
--   4 : QxDateTime Nullable addMonths(local(now()), 1, 'uq')
--   5 : QxNumeric Nullable year(cast('1900-04-29', 'date'), 'uq')
--   6 : QxDate Nullable addYears(cast('2077-11-01', 'date'), 1, 'uq')
