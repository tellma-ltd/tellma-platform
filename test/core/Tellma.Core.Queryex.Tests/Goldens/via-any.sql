SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE ([P1].[AgentId] IS NOT NULL)
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
