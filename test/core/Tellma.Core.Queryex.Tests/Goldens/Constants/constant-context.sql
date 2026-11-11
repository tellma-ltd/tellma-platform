SELECT
    DATEADD(MONTH, DATEDIFF(MONTH, CONVERT(date, '00010101'), @qx1_p0), CONVERT(date, '00010101')) AS [c0],
    @qx1_p1 AS [c1]
;

-- parameters
--   @qx1_p0 : QxDate QxDate Today
--   @qx1_p1 : QxNumeric QxDecimal UserId
-- columns
--   0 : QxDate NotNull startOfMonth(today())
--   1 : QxNumeric NotNull me()
