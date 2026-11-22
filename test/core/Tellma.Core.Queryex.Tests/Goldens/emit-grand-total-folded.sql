SELECT
    CASE WHEN (1 = 0) THEN 1 ELSE 0 END AS [c0],
    @qx0_p0 AS [c1]
FROM [gl].[Documents] AS [T]
HAVING (1 = 1)
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxBool NotNull count() is null
--   1 : QxNumeric NotNull 1
