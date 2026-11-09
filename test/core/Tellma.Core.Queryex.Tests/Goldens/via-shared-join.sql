SELECT
    [T].[DocumentId] AS [c0],
    [P1].[AgentName] AS [c1]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE (([P1].[AgentId] IS NOT NULL) AND ([P1].[AgentName] <> @qx0_p0))
ORDER BY [c0] ASC
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'beta'
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString Nullable path=Customer.Name Customer.Name
