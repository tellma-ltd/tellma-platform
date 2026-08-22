SELECT
    CONVERT(nvarchar(4000), [T].[Amount]) AS [c0],
    CAST(CONVERT(nvarchar(4000), [T].[Amount]) AS decimal(38, 6)) AS [c1],
    CONVERT(nvarchar(10), [T].[PostingDate], 23) AS [c2],
    CASE [T].[IsPosted] WHEN 1 THEN N'true' WHEN 0 THEN N'false' END AS [c3],
    LOWER(CONVERT(nvarchar(36), [T].[ExternalId])) AS [c4],
    CONVERT(date, [T].[Notes], 23) AS [c5],
    CAST(NULL AS decimal(38, 6)) AS [c6]
FROM [gl].[Documents] AS [T]
;

-- parameters
-- columns
--   0 : QxString NotNull cast(Amount, 'string')
--   1 : QxNumeric NotNull cast(cast(Amount, 'string'), 'numeric')
--   2 : QxString NotNull cast(PostingDate, 'string')
--   3 : QxString NotNull cast(IsPosted, 'string')
--   4 : QxString Nullable cast(ExternalId, 'string')
--   5 : QxDate Nullable cast(Notes, 'date')
--   6 : QxNumeric Null cast(null, 'numeric')
