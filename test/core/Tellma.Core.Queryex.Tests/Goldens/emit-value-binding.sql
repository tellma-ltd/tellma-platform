SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
CROSS APPLY (VALUES (([T].[Rate] + @qx0_p0))) AS [B1] ([v])
CROSS APPLY (VALUES ((CAST([T].[Memo] AS nvarchar(max)) + @qx0_p2))) AS [B2] ([v])
WHERE ((([B1].[v] IS NOT NULL) AND ([B1].[v] > @qx0_p1)) AND NOT (((([B2].[v] IS NULL) AND ([T].[Notes] IS NULL)) OR (([B2].[v] IS NOT NULL) AND ([T].[Notes] IS NOT NULL) AND ([B2].[v] = [T].[Notes])))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 2
--   @qx0_p2 : QxString QxNVarChar(4000) Literal = 'x'
-- columns
--   0 : QxNumeric NotNull path=Id Id
