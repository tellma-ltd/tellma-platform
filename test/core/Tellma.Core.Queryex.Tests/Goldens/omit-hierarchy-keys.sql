DECLARE @qx0_v0 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x] WHERE [x].[Concept] = @qx0_p0);

SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Accounts] AS [P1] ON [P1].[AccountId] = [T].[AccountId]
WHERE ((@qx0_v0 IS NOT NULL) AND ([P1].[TreeNode].IsDescendantOf(@qx0_v0) = 1))
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'Equity'
-- columns
--   0 : QxNumeric NotNull path=Id Id
