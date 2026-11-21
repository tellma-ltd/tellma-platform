SELECT
    [T].[Concept] AS [c0],
    [T].[TreeNode].GetLevel() AS [c1]
FROM [gl].[Accounts] AS [T]
WHERE ([T].[TreeNode].GetLevel() <= @qx0_p0)
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxString NotNull path=Concept Concept
--   1 : QxNumeric NotNull level(Node)
