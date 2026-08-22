SELECT
    ([T].[Amount] + @qx0_p0) AS [c0],
    ([T].[Amount] - @qx0_p0) AS [c1],
    ([T].[Amount] * @qx0_p1) AS [c2],
    ([T].[Amount] % @qx0_p1) AS [c3],
    (-[T].[Amount]) AS [c4],
    ABS([T].[Amount]) AS [c5],
    ROUND([T].[Amount], @qx0_p1) AS [c6],
    FLOOR([T].[Amount]) AS [c7],
    CEILING([T].[Amount]) AS [c8]
FROM [gl].[Documents] AS [T]
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 2
-- columns
--   0 : QxNumeric NotNull Amount + 1
--   1 : QxNumeric NotNull Amount - 1
--   2 : QxNumeric NotNull Amount * 2
--   3 : QxNumeric NotNull Amount % 2
--   4 : QxNumeric NotNull -Amount
--   5 : QxNumeric NotNull abs(Amount)
--   6 : QxNumeric NotNull round(Amount, 2)
--   7 : QxNumeric NotNull floor(Amount)
--   8 : QxNumeric NotNull ceiling(Amount)
