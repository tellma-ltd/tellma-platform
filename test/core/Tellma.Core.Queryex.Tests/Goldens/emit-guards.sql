SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE ((([T].[Rate] IS NOT NULL) AND ([T].[Rate] = @qx0_p0)) AND NOT (((([T].[Memo] IS NULL) AND ([T].[Notes] IS NULL)) OR (([T].[Memo] IS NOT NULL) AND ([T].[Notes] IS NOT NULL) AND ([T].[Memo] = [T].[Notes])))) AND (([T].[Rate] IS NULL) OR ([T].[Rate] <> [T].[Amount])) AND (([T].[Rate] IS NOT NULL) AND ([T].[Rate] > [T].[Amount])))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(3, 2) Literal = 1.25
-- columns
--   0 : QxNumeric NotNull path=Id Id
