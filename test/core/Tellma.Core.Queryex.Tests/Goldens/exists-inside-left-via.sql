SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE (([P1].[AgentId] IS NOT NULL) AND EXISTS (SELECT 1 FROM [gl].[Documents] AS [X1] WHERE (([X1].[AgentFk] = [P1].[AgentId]) AND ([X1].[IsPosted] = 1))))
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
