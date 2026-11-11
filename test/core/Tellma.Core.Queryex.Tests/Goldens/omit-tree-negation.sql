SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (((@qx0_p0 IS NOT NULL) AND ([T].[PostingDate] <= @qx0_p0)) AND NOT (([T].[Amount] > @qx0_p1)))
;

-- parameters
--   @qx0_p0 : QxDate QxDate Declared <- AsOf
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 0
-- columns
--   0 : QxNumeric NotNull path=Id Id
