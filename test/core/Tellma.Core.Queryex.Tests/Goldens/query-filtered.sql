SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[Amount] > @qx0_p0) AND ([T].[IsPosted] = 1))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(3, 0) Literal = 100
-- columns
--   0 : QxNumeric NotNull path=Id Id
