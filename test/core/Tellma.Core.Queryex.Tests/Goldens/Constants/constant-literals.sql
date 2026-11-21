SELECT
    (@qx0_p0 + (@qx0_p1 * @qx0_p2)) AS [c0],
    (CAST(@qx0_p3 AS nvarchar(max)) + @qx0_p4) AS [c1],
    CASE WHEN (1 = 1) THEN 1 ELSE 0 END AS [c2],
    @qx0_p5 AS [c3],
    @qx0_p6 AS [c4],
    @qx0_p7 AS [c5],
    @qx0_p8 AS [c6],
    CAST(NULL AS date) AS [c7],
    @qx0_p9 AS [c8]
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 2
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 3
--   @qx0_p3 : QxString QxNVarChar(4000) Literal = 'abc'
--   @qx0_p4 : QxString QxNVarChar(4000) Literal = 'def'
--   @qx0_p5 : QxDate QxDate Literal = 03/01/2024
--   @qx0_p6 : QxDateTime QxDateTime2(7) Literal = 03/01/2024 10:30:00
--   @qx0_p7 : QxDateTimeOffset QxDateTimeOffset(7) Literal = 03/01/2024 10:30:00 +03:00
--   @qx0_p8 : QxGuid QxUniqueIdentifier Literal = 6f9619ff-8b86-d011-b42d-00c04fc964ff
--   @qx0_p9 : QxString QxNVarChar(4000) Literal = 'spaced'
-- columns
--   0 : QxNumeric NotNull 1 + 2 * 3
--   1 : QxString NotNull 'abc' || 'def'
--   2 : QxBool NotNull not false
--   3 : QxDate NotNull '2024-03-01'
--   4 : QxDateTime NotNull '2024-03-01T10:30:00'
--   5 : QxDateTimeOffset NotNull '2024-03-01T10:30:00+03:00'
--   6 : QxGuid NotNull '6f9619ff-8b86-d011-b42d-00c04fc964ff'
--   7 : QxDate Null null
--   8 : QxString NotNull  ( 'spaced' ) 
