DECLARE @qx0_v0 date = (SELECT (SELECT [M2].[StartDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx0_p1 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = 1) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);

SELECT
    [T].[DocumentId] AS [c0],
    (DATEDIFF_BIG(HOUR, @qx0_v0, [T].[PostingDate]) / 24.0) AS [c1]
FROM [gl].[Documents] AS [T]
WHERE ((@qx0_v0 IS NOT NULL) AND ([T].[PostingDate] >= @qx0_v0))
OPTION (RECOMPILE)
;

-- parameters
--   @qx0_p0 : QxDate QxDate Today
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'uq'
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxNumeric Nullable diffDays(startOfYear(today(), 'uq'), PostingDate)
