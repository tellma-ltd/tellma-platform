SELECT
    [T].[DocumentId] AS [c0],
    [P1].[AgentName] AS [c1]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
CROSS APPLY (VALUES (([T].[Rate] + @qx0_p0))) AS [B1] ([v])
WHERE ((([B1].[v] IS NOT NULL) AND ([B1].[v] > @qx0_p1)) AND EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] LEFT JOIN [dbo].[Agents] AS [P2] ON [P2].[AgentId] = [X1].[AgentFk] CROSS APPLY (VALUES ((CAST([X1].[Memo] AS nvarchar(max)) + @qx0_p2))) AS [B2] ([v]) WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND ((([B2].[v] IS NULL) OR ([B2].[v] <> @qx0_p3)) AND (([P2].[AgentId] IS NOT NULL) AND ([P2].[AgentName] <> @qx0_p4))))) AND EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X2] CROSS APPLY (VALUES (([X2].[AgentFk] + @qx0_p0))) AS [B3] ([v]) WHERE (([X2].[DocumentId] = [T].[DocumentId]) AND (([B3].[v] IS NOT NULL) AND ([B3].[v] > @qx0_p1)))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 2
--   @qx0_p2 : QxString QxNVarChar(4000) Literal = 'x'
--   @qx0_p3 : QxString QxNVarChar(4000) Literal = 'firstx'
--   @qx0_p4 : QxString QxNVarChar(4000) Literal = 'beta'
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString Nullable path=Customer.Name Customer.Name
