SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[PostingDate] >= @qx0_p0) AND ((@qx0_p1 IS NOT NULL) AND ([T].[PostingDate] <= @qx0_p1)))
;

-- parameters
--   @qx0_p0 : QxDate QxDate Declared <- From
--   @qx0_p1 : QxDate QxDate Declared <- To
-- columns
--   0 : QxNumeric NotNull path=Id Id
