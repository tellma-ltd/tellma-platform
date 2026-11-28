SELECT
    [T].[DocCode] AS [c0],
    [T].[PostingDate] AS [c1]
FROM [gl].[Documents] AS [T]
ORDER BY [c1] DESC, [T].[DocumentId] ASC
OFFSET @qx0_skip ROWS FETCH NEXT @qx0_take ROWS ONLY
;

-- parameters
--   @qx0_skip : QxNumeric QxInt Literal = 2
--   @qx0_take : QxNumeric QxInt Literal = 3
-- columns
--   0 : QxString NotNull path=Code Code
--   1 : QxDate NotNull path=PostingDate PostingDate
