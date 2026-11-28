SELECT
    [T].[DocumentId] AS [c0],
    [T].[Amount] AS [c1],
    [P1].[AgentName] AS [c2],
    [T].[ExtrasJson] AS [c3]
FROM [gl].[Documents] AS [T]
LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxNumeric NotNull path=Amount Amount
--   2 : QxString Nullable path=Customer.Name Customer.Name
--   3 : QxString Nullable ExtrasJson
