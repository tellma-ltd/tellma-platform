SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [X1].[AgentFk] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND (([P1].[AgentId] IS NOT NULL) AND ([P1].[AgentName] = @qx0_p0))))
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'Acme'
-- columns
--   0 : QxNumeric NotNull path=Id Id
