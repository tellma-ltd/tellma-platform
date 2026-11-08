SELECT
    [T].[DocCode] AS [c0],
    [T].[PostingDate] AS [c1],
    [T].[ExtrasJson] AS [c2],
    [T].[AuditJson] AS [c3]
FROM [gl].[Documents] AS [T]
ORDER BY [c1] DESC, [T].[DocumentId] ASC
OFFSET @qx0_skip ROWS FETCH NEXT @qx0_take ROWS ONLY
;

-- parameters
--   @qx0_skip : QxNumeric QxInt Literal = 1
--   @qx0_take : QxNumeric QxInt Literal = 4
-- columns
--   0 : QxString NotNull path=Code Code
--   1 : QxDate NotNull path=PostingDate PostingDate
--   2 : QxString Nullable ExtrasJson
--   3 : QxString Nullable AuditJson
