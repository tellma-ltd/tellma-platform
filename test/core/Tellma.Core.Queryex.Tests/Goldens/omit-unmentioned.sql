SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE ((@qx0_p0 IS NOT NULL) AND ([T].[PostingDate] <= @qx0_p0))
;

-- parameters
--   @qx0_p0 : QxDate QxDate Declared <- AsOf
-- columns
--   0 : QxNumeric NotNull path=Id Id
