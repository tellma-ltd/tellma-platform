SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE ((([T].[AgentFk] IS NULL) AND (@qx0_p0 IS NULL)) OR (([T].[AgentFk] IS NOT NULL) AND (@qx0_p0 IS NOT NULL) AND ([T].[AgentFk] = @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal Declared <- Main
-- columns
--   0 : QxNumeric NotNull path=Id Id
