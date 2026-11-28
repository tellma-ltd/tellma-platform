SELECT
    [T].[DocumentId] AS [c0],
    CAST(NULL AS decimal(38, 6)) AS [c1],
    COALESCE(CAST(NULL AS decimal(38, 6)), [T].[AgentFk], @qx0_p0) AS [c2],
    CASE WHEN (([T].[AgentFk] IS NULL) OR ((([T].[AgentFk] IS NULL) AND (@qx0_p1 IS NULL)) OR (([T].[AgentFk] IS NOT NULL) AND (@qx0_p1 IS NOT NULL) AND ([T].[AgentFk] = @qx0_p1)))) THEN 1 ELSE 0 END AS [c3],
    CASE WHEN ([T].[AgentFk] IS NULL) THEN @qx0_p2 ELSE @qx0_p0 END AS [c4],
    CASE WHEN (1 = 0) THEN @qx0_p2 ELSE @qx0_p0 END AS [c5]
FROM [gl].[Documents] AS [T]
ORDER BY COALESCE(CAST(NULL AS decimal(38, 6)), [T].[Amount]) DESC
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p1 : QxNumeric QxDecimal Declared <- Main
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxNumeric Null @Customer
--   2 : QxNumeric NotNull coalesce(@Customer, CustomerId, 0)
--   3 : QxBool NotNull CustomerId in (@Main, @Other)
--   4 : QxNumeric NotNull if(CustomerId = @Customer, 1, 0)
--   5 : QxNumeric NotNull if(@Posted, 1, 0)
