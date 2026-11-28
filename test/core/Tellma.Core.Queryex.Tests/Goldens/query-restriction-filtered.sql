SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[IsPosted] = 1) AND ([T].[AgentFk] IN (SELECT [Id] FROM @tb0_parties)))
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
