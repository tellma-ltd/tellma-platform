SELECT
    [P1].[SegmentName] AS [c0],
    COUNT_BIG(*) AS [c1]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
ORDER BY (SELECT [M].[Month] FROM (VALUES (MAX([T].[PostingDate]))) AS [V1] ([v]) CROSS APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [V1].[v] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [V1].[v] < [M].[EndDate]) ASC
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric NotNull count()
