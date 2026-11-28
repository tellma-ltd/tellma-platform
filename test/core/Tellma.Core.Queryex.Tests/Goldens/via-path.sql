SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
LEFT JOIN [dbo].[Regions] AS [P2] ON [P2].[Id] = [P1].[RegionId]
WHERE (([P2].[Id] IS NOT NULL) AND ([P2].[Name] = @qx0_p0))
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'North'
-- columns
--   0 : QxNumeric NotNull path=Id Id
