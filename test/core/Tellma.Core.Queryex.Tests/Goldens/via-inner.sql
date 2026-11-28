SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
WHERE ([P1].[SegmentName] = @qx0_p0)
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'Sales'
-- columns
--   0 : QxNumeric NotNull path=Id Id
