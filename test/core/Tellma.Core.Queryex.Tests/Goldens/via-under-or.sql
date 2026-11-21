SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE (([T].[Amount] < @qx0_p0) OR (([P1].[AgentId] IS NOT NULL) AND (([P1].[Gender] IS NOT NULL) AND ([P1].[Gender] = @qx0_p1))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p1 : QxString QxNVarChar(4000) Literal = 'F'
-- columns
--   0 : QxNumeric NotNull path=Id Id
