SELECT
    [T].[LineId] AS [c0],
    [T].[LineAmount] AS [c1]
FROM [gl].[DocumentLines] AS [T]
INNER JOIN [gl].[Documents] AS [P1] ON [P1].[DocumentId] = [T].[DocumentId]
WHERE ([P1].[IsPosted] = 1)
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxNumeric NotNull path=Amount Amount
