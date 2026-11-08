SELECT
    [T].[AgentId] AS [c0],
    [T].[AgentName] AS [c1]
FROM [dbo].[Agents] AS [T]
WHERE EXISTS (SELECT 1 FROM [gl].[Documents] AS [X1] WHERE (([X1].[AgentFk] = [T].[AgentId]) AND EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X2] WHERE (([X2].[DocumentId] = [X1].[DocumentId]) AND ([X2].[LineAmount] >= @qx0_p0)))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(2, 0) Literal = 20
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString NotNull path=Name Name
