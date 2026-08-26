SELECT
    @qx0_p1 AS [c0],
    @qx0_p2 AS [c1]
FROM [gl].[Documents] AS [T]
WHERE ([T].[CreatedById] = @qx0_p0)
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal UserId
--   @qx0_p1 : QxDate QxDate Today
--   @qx0_p2 : QxDateTimeOffset QxDateTimeOffset(7) Now
-- columns
--   0 : QxDate NotNull today()
--   1 : QxDateTimeOffset NotNull now()
