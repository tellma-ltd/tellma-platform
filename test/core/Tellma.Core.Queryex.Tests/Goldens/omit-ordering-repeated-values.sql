SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
ORDER BY COALESCE(CAST(NULL AS decimal(38, 6)), [T].[Amount]) DESC, [c0] ASC
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
