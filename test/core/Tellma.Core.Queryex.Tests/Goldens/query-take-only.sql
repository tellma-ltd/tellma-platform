SELECT
    [T].[DocCode] AS [c0]
FROM [gl].[Documents] AS [T]
ORDER BY [c0] DESC, [T].[DocumentId] ASC
OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
;

-- parameters
--   @qx0_take : QxNumeric QxInt Literal = 2
-- columns
--   0 : QxString NotNull path=Code Code
