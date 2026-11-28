SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[IsPosted] = 1) OR EXISTS (SELECT 1 FROM [gl].[DocumentLines] AS [X1] WHERE (([X1].[DocumentId] = [T].[DocumentId]) AND ([X1].[Memo] IS NULL))))
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
