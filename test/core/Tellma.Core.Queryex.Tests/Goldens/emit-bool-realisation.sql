SELECT
    [T].[IsPosted] AS [c0],
    [T].[IsApproved] AS [c1],
    CASE WHEN ([T].[Amount] > @qx0_p1) THEN 1 ELSE 0 END AS [c2],
    CASE WHEN ([T].[Amount] > @qx0_p0) THEN [T].[IsPosted] ELSE 0 END AS [c3]
FROM [gl].[Documents] AS [T]
WHERE (CASE WHEN ([T].[Amount] > @qx0_p0) THEN [T].[IsPosted] ELSE 0 END = 1)
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p1 : QxNumeric QxDecimal(3, 0) Literal = 100
-- columns
--   0 : QxBool NotNull path=IsPosted IsPosted
--   1 : QxBool Nullable path=IsApproved IsApproved
--   2 : QxBool NotNull Amount > 100
--   3 : QxBool NotNull if(Amount > 0, IsPosted, false)
