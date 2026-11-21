SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
LEFT JOIN [dbo].[Agents] AS [P2] ON [P2].[AgentId] = [P1].[ManagerId]
WHERE (([P1].[AgentId] IS NOT NULL) AND (([P2].[AgentId] IS NOT NULL) AND ([P2].[AgentName] = @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'Acme'
-- columns
--   0 : QxNumeric NotNull path=Id Id
