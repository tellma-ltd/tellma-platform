DECLARE @qx0_v0 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x] WHERE [x].[Concept] = @qx0_p0);
DECLARE @qx0_v1 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x] WHERE [x].[Concept] = @qx0_p1);
DECLARE @qx0_v2 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x] WHERE [x].[Concept] = @qx0_p2);

SELECT
    [T].[DocumentId] AS [c0]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Accounts] AS [P1] ON [P1].[AccountId] = [T].[AccountId]
WHERE ((((@qx0_v0 IS NOT NULL) AND ([P1].[TreeNode].IsDescendantOf(@qx0_v0) = 1)) OR ((@qx0_v1 IS NOT NULL) AND ([P1].[TreeNode].IsDescendantOf(@qx0_v1) = 1))) OR ((@qx0_v2 IS NOT NULL) AND (@qx0_v2.IsDescendantOf([P1].[TreeNode]) = 1)))
;

-- parameters
--   @qx0_p0 : QxString QxVarChar(8000) Literal = 'Assets'
--   @qx0_p1 : QxString QxVarChar(8000) Literal = 'Equity'
--   @qx0_p2 : QxString QxVarChar(8000) Literal = 'Cash'
-- columns
--   0 : QxNumeric NotNull path=Id Id
