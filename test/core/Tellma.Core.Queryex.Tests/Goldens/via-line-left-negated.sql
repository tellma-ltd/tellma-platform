SELECT
    [T].[LineId] AS [c0]
FROM [gl].[DocumentLines] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE NOT ((([P1].[AgentId] IS NOT NULL) AND ([P1].[AgentName] = @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'Acme'
-- columns
--   0 : QxNumeric NotNull path=Id Id
