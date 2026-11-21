DECLARE @qx0_v0 date = (SELECT [M].[StartDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);

SELECT
    SUM(CASE WHEN ((@qx0_v0 IS NOT NULL) AND ([P1].[PostingDate] >= @qx0_v0)) THEN [T].[LineAmount] END) AS [c0]
FROM [gl].[DocumentLines] AS [T]
INNER JOIN [gl].[Documents] AS [P1] ON [P1].[DocumentId] = [T].[DocumentId]
;

-- parameters
--   @qx0_p0 : QxDate QxDate Today
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxNumeric Nullable sum(Amount, Invoice.PostingDate >= startOfMonth(today(), 'uq'))
