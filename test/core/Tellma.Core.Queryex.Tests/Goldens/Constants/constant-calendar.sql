SELECT
    DATEADD(MONTH, @qx3_p1, DATEADD(YEAR, DATEDIFF(YEAR, CONVERT(date, '00010101'), @qx3_p0), CONVERT(date, '00010101'))) AS [c0],
    ((DATEPART(YEAR, @qx3_p0) * @qx3_p2) + DATEPART(MONTH, @qx3_p0)) AS [c1],
    @qx3_p3 AS [c2],
    CAST(@qx3_p3 AT TIME ZONE @qx3_p4 AS datetime2(7)) AS [c3],
    (DATEDIFF_BIG(HOUR, @qx3_p5, @qx3_p0) / 24.0) AS [c4]
;

-- parameters
--   @qx3_p0 : QxDate QxDate Today
--   @qx3_p1 : QxNumeric QxDecimal(1, 0) Literal = 6
--   @qx3_p2 : QxNumeric QxDecimal(3, 0) Literal = 100
--   @qx3_p3 : QxDateTimeOffset QxDateTimeOffset(7) Now
--   @qx3_p4 : QxString QxNVarChar(4000) TimeZone
--   @qx3_p5 : QxDate QxDate Literal = 01/01/2024
-- columns
--   0 : QxDate NotNull addMonths(startOfYear(today()), 6)
--   1 : QxNumeric NotNull year(today()) * 100 + month(today())
--   2 : QxDateTimeOffset NotNull now()
--   3 : QxDateTime NotNull local(now())
--   4 : QxNumeric NotNull diffDays('2024-01-01', today())
