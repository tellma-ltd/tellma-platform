SELECT
    [P1].[TreeNode].GetLevel() AS [c0],
    [T].[AltNode].GetLevel() AS [c1],
    [P2].[TreeNode].GetLevel() AS [c2]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Accounts] AS [P1] ON [P1].[AccountId] = [T].[AccountId]
LEFT JOIN [gl].[Accounts] AS [P2] ON [P2].[AccountId] = [P1].[ParentId]
WHERE ([P1].[TreeNode].GetLevel() <= @qx0_p0)
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxNumeric NotNull level(Account.Node)
--   1 : QxNumeric Nullable level(AltNode)
--   2 : QxNumeric Nullable level(Account.Parent.Node)
