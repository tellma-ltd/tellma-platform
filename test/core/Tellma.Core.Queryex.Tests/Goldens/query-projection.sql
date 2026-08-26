SELECT
    [T].[DocumentId] AS [c0],
    [T].[DocCode] AS [c1],
    [T].[Amount] AS [c2]
FROM [gl].[Documents] AS [T]
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString NotNull path=Code Code
--   2 : QxNumeric NotNull path=Amount Amount
