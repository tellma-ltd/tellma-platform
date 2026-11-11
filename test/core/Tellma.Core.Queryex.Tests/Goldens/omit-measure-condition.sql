SELECT
    [P1].[SegmentName] AS [c0],
    SUM(CASE WHEN (1 = 1) THEN [T].[Amount] END) AS [c1],
    COUNT_BIG(CASE WHEN ((([T].[AgentFk] IS NULL) AND (@qx0_p0 IS NULL)) OR (([T].[AgentFk] IS NOT NULL) AND (@qx0_p0 IS NOT NULL) AND ([T].[AgentFk] = @qx0_p0))) THEN [T].[DocumentId] END) AS [c2]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
ORDER BY [c0] ASC
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal Declared <- Main
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxNumeric Nullable sum(Amount, CustomerId = @Customer)
--   2 : QxNumeric NotNull count(Id, CustomerId = @Main)
