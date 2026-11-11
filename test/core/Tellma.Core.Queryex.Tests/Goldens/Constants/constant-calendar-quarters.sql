SELECT
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), CONVERT(date, @qx0_p0, 23)), CONVERT(date, '00010101')) AS [c0],
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), CONVERT(date, @qx0_p1, 23)), CONVERT(date, '00010101')) AS [c1],
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), CONVERT(date, @qx0_p2, 23)), CONVERT(date, '00010101')) AS [c2],
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), CONVERT(date, @qx0_p3, 23)), CONVERT(date, '00010101')) AS [c3],
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), CONVERT(date, @qx0_p4, 23)), CONVERT(date, '00010101')) AS [c4],
    DATEADD(YEAR, @qx0_p5, CONVERT(date, @qx0_p2, 23)) AS [c5],
    (SELECT (SELECT [M2].[StartDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx0_p6 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = ((([M].[Month] - 1) / 3) * 3 + 1)) FROM (VALUES (CONVERT(date, @qx0_p2, 23))) AS [V1] ([v]) CROSS APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p6 AND [M].[StartDate] <= [V1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [V1].[v] < [M].[EndDate]) AS [c6],
    DATEADD(DAY, DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) - ((DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) - (365 * (((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) + 1463) / 1461) - 1) + ((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) + 1463) / 1461) / 4)) + 90 * LEAST(((DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) - (365 * (((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) + 1463) / 1461) - 1) + ((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), CONVERT(date, @qx0_p7, 23)) + 2922) + 1463) / 1461) / 4)) / 90, 3), CONVERT(date, '00080827')) AS [c7]
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = '2023-12-31'
--   @qx0_p1 : QxString QxNVarChar(4000) Literal = '2024-01-01'
--   @qx0_p2 : QxString QxNVarChar(4000) Literal = '2024-02-29'
--   @qx0_p3 : QxString QxNVarChar(4000) Literal = '2024-03-31'
--   @qx0_p4 : QxString QxNVarChar(4000) Literal = '2024-04-01'
--   @qx0_p5 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p6 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p7 : QxString QxNVarChar(4000) Literal = '2026-09-10'
-- columns
--   0 : QxDate NotNull startOfQuarter(cast('2023-12-31', 'date'))
--   1 : QxDate NotNull startOfQuarter(cast('2024-01-01', 'date'))
--   2 : QxDate NotNull startOfQuarter(cast('2024-02-29', 'date'))
--   3 : QxDate NotNull startOfQuarter(cast('2024-03-31', 'date'))
--   4 : QxDate NotNull startOfQuarter(cast('2024-04-01', 'date'))
--   5 : QxDate NotNull addYears(cast('2024-02-29', 'date'), 1)
--   6 : QxDate Nullable startOfQuarter(cast('2024-02-29', 'date'), 'uq')
--   7 : QxDate NotNull startOfQuarter(cast('2026-09-10', 'date'), 'et')
