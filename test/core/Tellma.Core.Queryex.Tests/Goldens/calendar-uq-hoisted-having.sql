DECLARE @qx0_v0 date = (SELECT (SELECT [M2].[StartDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx0_p1 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = 1) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);
DECLARE @qx0_v1 date = (SELECT (SELECT DATEADD(DAY, DATEDIFF(DAY, [M].[StartDate], [M2].[StartDate]) + LEAST(0, DATEDIFF(DAY, [M2].[StartDate], [M2].[EndDate]) - 1 - DATEDIFF(DAY, [M].[StartDate], @qx0_v0)), @qx0_v0) FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx0_p1 AND [M2].[Year] = (([M].[Year] * 12 + [M].[Month] - 1 + CAST(@qx0_p2 AS int)) / 12) AND [M2].[Month] = (([M].[Year] * 12 + [M].[Month] - 1 + CAST(@qx0_p2 AS int)) % 12 + 1)) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_v0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_v0 < [M].[EndDate]);

SELECT
    [P1].[SegmentName] AS [c0],
    COUNT_BIG(*) AS [c1]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
HAVING ((@qx0_v1 IS NOT NULL) AND (MAX([T].[PostingDate]) >= @qx0_v1))
OPTION (RECOMPILE)
;

-- parameters
--   @qx0_p0 : QxDate QxDate Today
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p2 : QxNumeric QxDecimal(2, 0) Literal = 10
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull count()
