SELECT
    [P2].[SegmentName] AS [c0],
    [P1].[AgentName] AS [c1]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
INNER JOIN [gl].[Segments] AS [P2] ON [P2].[SegmentId] = [T].[SegmentId]
GROUP BY [P2].[SegmentName], [P1].[AgentName]
;

-- parameters
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxString Nullable key path=Customer.Name Customer.Name
