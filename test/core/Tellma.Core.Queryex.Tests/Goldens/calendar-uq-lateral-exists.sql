SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] INNER JOIN [gl].[Documents] AS [P1] ON [P1].[DocumentId] = [X1].[DocumentId] OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [P1].[DueDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [P1].[DueDate] < [M].[EndDate]) AS [C1] OUTER APPLY (SELECT [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [P1].[PostingDate] ORDER BY [M].[StartDate] DESC) AS [M] WHERE [P1].[PostingDate] < [M].[EndDate]) AS [C2] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND (([C1].[Month] IS NOT NULL) AND ([C2].[Month] IS NOT NULL) AND ([C1].[Month] >= [C2].[Month]))))
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxNumeric NotNull path=Id Id
