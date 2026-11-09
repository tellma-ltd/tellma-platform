SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND ([X1].[LineAmount] > @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(2, 0) Literal = 10
-- columns
--   0 : QxNumeric NotNull path=Id Id
