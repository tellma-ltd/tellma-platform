SELECT
    [T].[LineId] AS [c0]
FROM [gl].[DocumentLines] AS [T]
INNER JOIN [gl].[Documents] AS [P1] ON [P1].[DocumentId] = [T].[DocumentId]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE (([X1].[DocumentId] = [P1].[DocumentId]) AND ([X1].[LineAmount] < @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
-- columns
--   0 : QxNumeric NotNull path=Id Id
