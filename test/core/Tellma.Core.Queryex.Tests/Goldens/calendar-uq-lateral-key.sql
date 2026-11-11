SELECT
    [C1].[Month] AS [c0],
    SUM([T].[LineAmount]) AS [c1]
FROM [gl].[DocumentLines] AS [T]
INNER JOIN [gl].[Documents] AS [P1] ON [P1].[DocumentId] = [T].[DocumentId]
OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [P1].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [P1].[PostingDate] < [M].[EndDate]) AS [C1]
GROUP BY [C1].[Month]
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxNumeric Nullable key month(Invoice.PostingDate, 'uq')
--   1 : QxNumeric NotNull sum(Amount)
