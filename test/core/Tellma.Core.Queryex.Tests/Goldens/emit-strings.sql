SELECT
    (CAST([T].[Memo] AS nvarchar(max)) + [T].[Notes]) AS [c0],
    LEN([T].[DocCode]) AS [c1],
    UPPER(TRIM([T].[Memo])) AS [c2],
    LEFT([T].[Memo], @qx0_p3) AS [c3],
    REPLACE([T].[Memo], @qx0_p4, @qx0_p5) AS [c4]
FROM [gl].[Documents] AS [T]
WHERE ((ISNULL(CHARINDEX(@qx0_p0, [T].[Memo]), 0) > 0 OR (ISNULL(DATALENGTH(@qx0_p0), 1) = 0 AND [T].[Memo] IS NOT NULL)) AND (ISNULL(CHARINDEX(@qx0_p1, [T].[DocCode]), 0) = 1 OR (ISNULL(DATALENGTH(@qx0_p1), 1) = 0 AND [T].[DocCode] IS NOT NULL)) AND (ISNULL(CHARINDEX(REVERSE(@qx0_p2), REVERSE([T].[DocRef])), 0) = 1 OR (ISNULL(DATALENGTH(@qx0_p2), 1) = 0 AND [T].[DocRef] IS NOT NULL)))
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) Literal = 'em'
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'INV'
--   @qx0_p2 : QxString QxVarChar(8000) Literal = ' '
--   @qx0_p3 : QxNumeric QxDecimal(1, 0) Literal = 3
--   @qx0_p4 : QxString QxNVarChar(4000) Literal = 'a'
--   @qx0_p5 : QxString QxNVarChar(4000) Literal = 'b'
-- columns
--   0 : QxString Nullable Memo || Notes
--   1 : QxNumeric NotNull length(Code)
--   2 : QxString Nullable upper(trim(Memo))
--   3 : QxString Nullable left(Memo, 3)
--   4 : QxString Nullable replace(Memo, 'a', 'b')
