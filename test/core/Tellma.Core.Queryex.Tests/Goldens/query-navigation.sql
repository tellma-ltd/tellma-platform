SELECT
    [P1].[AgentName] AS [c0],
    [P4].[SegmentName] AS [c1],
    [P3].[Name] AS [c2]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
LEFT JOIN [dbo].[Agents] AS [P2] ON [P2].[AgentId] = [P1].[ManagerId]
LEFT JOIN [dbo].[Regions] AS [P3] ON [P3].[Id] = [P2].[RegionId]
INNER JOIN [gl].[Segments] AS [P4] ON [P4].[SegmentId] = [T].[SegmentId]
;

-- parameters
-- columns
--   0 : QxString Nullable path=Customer.Name Customer.Name
--   1 : QxString NotNull path=Centre.Name Centre.Name
--   2 : QxString Nullable path=Customer.Manager.Region.Name Customer.Manager.Region.Name
