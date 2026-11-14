SELECT
    @qx0_p0 AS [c0],
    COALESCE(@qx0_p0, (-@qx0_p1)) AS [c1],
    CASE WHEN (1 = 0) THEN 1 ELSE 0 END AS [c2]
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal UserId
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxNumeric Null me()
--   1 : QxNumeric NotNull coalesce(me(), -1)
--   2 : QxBool NotNull me() = 2
