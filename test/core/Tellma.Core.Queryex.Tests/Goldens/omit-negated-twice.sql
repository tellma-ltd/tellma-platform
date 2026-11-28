SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE NOT (([T].[IsPosted] = 1))
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
