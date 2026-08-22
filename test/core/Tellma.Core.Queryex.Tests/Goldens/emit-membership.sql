SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
WHERE (([T].[LineCount] IN (@qx0_p0, @qx0_p1, @qx0_p2)) AND ((([T].[Rate] IS NOT NULL) AND ([T].[Rate] IN (@qx0_p3, @qx0_p4))) OR ([T].[Rate] IS NULL)) AND ((([T].[Memo] IS NOT NULL) AND ([T].[Memo] IN (@qx0_p5))) OR ((([T].[Memo] IS NULL) AND ([T].[Notes] IS NULL)) OR (([T].[Memo] IS NOT NULL) AND ([T].[Notes] IS NOT NULL) AND ([T].[Memo] = [T].[Notes])))))
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 3
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 6
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 9
--   @qx0_p3 : QxNumeric QxDecimal(3, 2) Literal = 1.25
--   @qx0_p4 : QxNumeric QxDecimal(1, 0) Literal = 2
--   @qx0_p5 : QxString QxNVarChar(4000) Literal = 'alpha memo'
-- columns
--   0 : QxNumeric NotNull path=Id Id
