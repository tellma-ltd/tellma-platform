SELECT
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), [T].[PostingDate]), CONVERT(date, '00010101')) AS [c0],
    DATEADD(QUARTER, DATEDIFF(QUARTER, CONVERT(date, '00010101'), [T].[PostedOn]), CONVERT(date, '00010101')) AS [c1],
    DATEADD(DAY, DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) - ((DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) - (365 * (((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) + 1463) / 1461) - 1) + ((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) + 1463) / 1461) / 4)) + 90 * LEAST(((DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) - (365 * (((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) + 1463) / 1461) - 1) + ((4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), [T].[PostingDate]) + 2922) + 1463) / 1461) / 4)) / 90, 3), CONVERT(date, '00080827')) AS [c2],
    [C2].[StartDate] AS [c3]
FROM [gl].[Documents] AS [T]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [T].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [T].[PostingDate] < [M].[EndDate]) AS [C1]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[Year] = [C1].[Year] AND [M].[Month] = ((([C1].[Month] - 1) / 3) * 3 + 1)) AS [C2]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxDate NotNull startOfQuarter(PostingDate)
--   1 : QxDate NotNull startOfQuarter(PostedOn, 'gc')
--   2 : QxDate NotNull startOfQuarter(PostingDate, 'et')
--   3 : QxDate Nullable startOfQuarter(PostingDate, 'uq')
