SELECT
    CASE WHEN EXISTS (SELECT CASE WHEN (DATEPART(DAY, @qx0_p0) > @qx0_p1) THEN @qx0_p2 ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT SELECT CASE WHEN (DATEPART(MONTH, @qx0_p0) > @qx0_p3) THEN @qx0_p2 ELSE CAST(NULL AS decimal(38, 6)) END) THEN 1 ELSE 0 END AS [c0],
    CASE WHEN NOT EXISTS (SELECT CASE WHEN (DATEPART(DAY, @qx0_p0) > @qx0_p4) THEN @qx0_p5 ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT SELECT CASE WHEN (DATEPART(MONTH, @qx0_p0) > @qx0_p3) THEN @qx0_p2 ELSE CAST(NULL AS decimal(38, 6)) END) THEN 1 ELSE 0 END AS [c1],
    CASE WHEN (CASE WHEN (CASE WHEN (DATEPART(DAY, @qx0_p0) > @qx0_p4) THEN (@qx0_p5 + @qx0_p2) ELSE CAST(NULL AS decimal(38, 6)) END < CASE WHEN (DATEPART(MONTH, @qx0_p0) > @qx0_p4) THEN @qx0_p6 ELSE CAST(NULL AS decimal(38, 6)) END) THEN 1 ELSE 0 END = 1) THEN 1 ELSE 0 END AS [c2],
    CASE WHEN EXISTS (SELECT CASE WHEN (DATEPART(DAY, @qx0_p0) > @qx0_p4) THEN @qx0_p7 ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT (SELECT CASE WHEN (DATEPART(MONTH, @qx0_p0) > @qx0_p3) THEN @qx0_p2 ELSE CAST(NULL AS decimal(38, 6)) END UNION ALL SELECT (@qx0_p5 + @qx0_p6) UNION ALL SELECT @qx0_p7)) THEN 1 ELSE 0 END AS [c3],
    CASE WHEN EXISTS (SELECT CASE WHEN (DATEPART(DAY, @qx0_p0) > @qx0_p1) THEN @qx0_p7 ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT (SELECT CASE WHEN (DATEPART(MONTH, @qx0_p0) > @qx0_p3) THEN @qx0_p2 ELSE CAST(NULL AS decimal(38, 6)) END UNION ALL SELECT @qx0_p8)) THEN 1 ELSE 0 END AS [c4],
    CASE WHEN (DATEADD(DAY, @qx0_p2, @qx0_p0) > @qx0_p0) THEN @qx0_p9 ELSE @qx0_p10 END AS [c5],
    (@qx0_p11 / (@qx0_p5 + @qx0_p12)) AS [c6]
;

-- parameters
--   @qx0_p0 : QxDate QxDate Today
--   @qx0_p1 : QxNumeric QxDecimal(2, 0) Literal = 40
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p3 : QxNumeric QxDecimal(2, 0) Literal = 12
--   @qx0_p4 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p5 : QxNumeric QxDecimal UserId
--   @qx0_p6 : QxNumeric QxDecimal(1, 0) Literal = 5
--   @qx0_p7 : QxNumeric QxDecimal(1, 0) Literal = 7
--   @qx0_p8 : QxNumeric QxDecimal(1, 0) Literal = 3
--   @qx0_p9 : QxString QxNVarChar(4000) Literal = 'later'
--   @qx0_p10 : QxString QxNVarChar(4000) Literal = 'never'
--   @qx0_p11 : QxNumeric QxDecimal(2, 0) Literal = 10
--   @qx0_p12 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxBool NotNull if(day(today()) > 40, 1, null) = if(month(today()) > 12, 1, null)
--   1 : QxBool NotNull if(day(today()) > 0, me(), null) != if(month(today()) > 12, 1, null)
--   2 : QxBool NotNull if(day(today()) > 0, me() + 1, null) < if(month(today()) > 0, 5, null)
--   3 : QxBool NotNull if(day(today()) > 0, 7, null) in (if(month(today()) > 12, 1, null), me() + 5, 7)
--   4 : QxBool NotNull if(day(today()) > 40, 7, null) in (if(month(today()) > 12, 1, null), 3)
--   5 : QxString NotNull if(addDays(today(), 1) > today(), 'later', 'never')
--   6 : QxNumeric NotNull 10 / (me() + 2)
