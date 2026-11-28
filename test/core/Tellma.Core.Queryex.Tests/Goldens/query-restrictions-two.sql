SELECT
    [T].[DocumentId] AS [c0],
    [P1].[AgentName] AS [c1]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
WHERE ((([T].[Amount] > @qx0_p0) OR ([T].[IsPosted] = 1)) AND ([P1].[AgentId] IN (SELECT [Id] FROM @tb0_parties)) AND ([T].[DocumentId] IN (SELECT [Id] FROM @tb0_invoices)))
ORDER BY [c1] DESC
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString Nullable path=Customer.Name Customer.Name
