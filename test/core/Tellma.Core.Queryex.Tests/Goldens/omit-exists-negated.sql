SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[IsPosted] = 1) AND EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND ((([X1].[AgentFk] IS NULL) AND (@qx0_p0 IS NULL)) OR (([X1].[AgentFk] IS NOT NULL) AND (@qx0_p0 IS NOT NULL) AND ([X1].[AgentFk] = @qx0_p0))))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal Declared <- Main
-- columns
--   0 : QxNumeric NotNull path=Id Id
