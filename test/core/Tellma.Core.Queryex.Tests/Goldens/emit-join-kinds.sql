SELECT
    [P4].[Name] AS [c0],
    [P2].[Name] AS [c1]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
LEFT JOIN [dbo].[Regions] AS [P2] ON [P2].[Id] = [P1].[RegionId]
INNER JOIN [gl].[Segments] AS [P3] ON [P3].[SegmentId] = [T].[SegmentId]
INNER JOIN [dbo].[Regions] AS [P4] ON [P4].[Id] = [P3].[RegionId]
;

-- parameters
-- columns
--   0 : QxString NotNull path=Centre.Region.Name Centre.Region.Name
--   1 : QxString Nullable path=Customer.Region.Name Customer.Region.Name
