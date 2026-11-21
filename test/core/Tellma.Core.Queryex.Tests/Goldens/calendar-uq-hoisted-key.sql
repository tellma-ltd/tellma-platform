DECLARE @qx0_v0 date = (SELECT (SELECT [M2].[StartDate] FROM [core].[CalendarMonths] AS [M2] WHERE [M2].[Calendar] = @qx0_p1 AND [M2].[Year] = [M].[Year] AND [M2].[Month] = ((([M].[Month] - 1) / 3) * 3 + 1)) FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);

SELECT
    CASE WHEN ((@qx0_v0 IS NOT NULL) AND ([T].[PostingDate] >= @qx0_v0)) THEN @qx0_p2 ELSE @qx0_p3 END AS [c0],
    COUNT_BIG(*) AS [c1]
FROM [gl].[Documents] AS [T]
GROUP BY CASE WHEN ((@qx0_v0 IS NOT NULL) AND ([T].[PostingDate] >= @qx0_v0)) THEN @qx0_p2 ELSE @qx0_p3 END
;

-- parameters
--   @qx0_p0 : QxDate QxDate Today
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'uq'
--   @qx0_p2 : QxString QxNVarChar(4000) Literal = 'this quarter'
--   @qx0_p3 : QxString QxNVarChar(4000) Literal = 'earlier'
-- columns
--   0 : QxString NotNull key if(PostingDate >= startOfQuarter(today(), 'uq'), 'this quarter', 'earlier')
--   1 : QxNumeric NotNull count()
