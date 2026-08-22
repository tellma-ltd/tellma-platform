SELECT
    CAST([T].[PostedAt] AT TIME ZONE @qx0_p0 AS datetime2(7)) AS [c0],
    CAST([T].[PostedAt] AT TIME ZONE @qx0_p1 AS datetime2(7)) AS [c1],
    DATEPART(HOUR, CAST([T].[PostedAt] AT TIME ZONE @qx0_p0 AS datetime2(7))) AS [c2],
    (DATEDIFF_BIG(HOUR, [T].[PostingDate], [T].[DueDate]) / 24.0) AS [c3],
    DATEDIFF_BIG(SECOND, [T].[PostedAt], [T].[ApprovedAt]) AS [c4],
    (DATEDIFF(YEAR, [T].[PostingDate], [T].[DueDate]) + CASE WHEN DATEDIFF(YEAR, [T].[PostingDate], [T].[DueDate]) > 0 AND DATEADD(YEAR, DATEDIFF(YEAR, [T].[PostingDate], [T].[DueDate]), [T].[PostingDate]) > [T].[DueDate] THEN -1 WHEN DATEDIFF(YEAR, [T].[PostingDate], [T].[DueDate]) < 0 AND DATEADD(YEAR, DATEDIFF(YEAR, [T].[PostingDate], [T].[DueDate]), [T].[PostingDate]) < [T].[DueDate] THEN 1 ELSE 0 END) AS [c5],
    DATEADD(DAY, @qx0_p2, [T].[PostedAt]) AS [c6],
    DATEADD(MONTH, @qx0_p2, [T].[PostingDate]) AS [c7]
FROM [gl].[Documents] AS [T]
;

-- parameters
--   @qx0_p0 : QxString QxNVarChar(4000) TimeZone
--   @qx0_p1 : QxString QxNVarChar(4000) Literal = 'E. Africa Standard Time'
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 1
-- columns
--   0 : QxDateTime Nullable local(PostedAt)
--   1 : QxDateTime Nullable local(PostedAt, 'Africa/Nairobi')
--   2 : QxNumeric Nullable hour(local(PostedAt))
--   3 : QxNumeric Nullable diffDays(PostingDate, DueDate)
--   4 : QxNumeric Nullable diffSeconds(PostedAt, ApprovedAt)
--   5 : QxNumeric Nullable diffYears(PostingDate, DueDate)
--   6 : QxDateTimeOffset Nullable addDays(PostedAt, 1)
--   7 : QxDate NotNull addMonths(PostingDate, 1)
