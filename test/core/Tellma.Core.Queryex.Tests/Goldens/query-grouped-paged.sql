SELECT
    [P1].[AgentName] AS [c0],
    [P2].[SegmentName] AS [c1],
    SUM([T].[Amount]) AS [c2]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
INNER JOIN [gl].[Segments] AS [P2] ON [P2].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[AgentName], [P2].[SegmentName]
ORDER BY [c2] DESC, [c0] ASC, [c1] ASC
OFFSET @qx0_p0 ROWS FETCH NEXT @qx0_p1 ROWS ONLY
;

-- parameters
--   @qx0_p0 : QxNumeric QxInt Literal = 0
--   @qx0_p1 : QxNumeric QxInt Literal = 25
-- columns
--   0 : QxString Nullable key path=Customer.Name Customer.Name
--   1 : QxString NotNull key path=Centre.Name Centre.Name
--   2 : QxNumeric NotNull sum(Amount)
