SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE ((([T].[IsPosted] = 1) OR ([T].[Amount] > @qx0_p0)) AND NOT (([T].[LineCount] > @qx0_p1)))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p1 : QxNumeric QxDecimal(2, 0) Literal = 10
-- columns
--   0 : QxNumeric NotNull path=Id Id
