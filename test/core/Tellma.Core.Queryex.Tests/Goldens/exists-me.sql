SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND ([X1].[CreatedById] = @qx0_p0)))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal UserId
-- columns
--   0 : QxNumeric NotNull path=Id Id
