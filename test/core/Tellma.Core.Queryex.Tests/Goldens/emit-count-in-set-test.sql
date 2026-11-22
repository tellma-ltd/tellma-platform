SELECT
    [P1].[SegmentName] AS [c0],
    CASE WHEN EXISTS (SELECT CASE WHEN (COUNT_BIG([T].[DocumentId]) > @qx0_p0) THEN COUNT_BIG([T].[DocumentId]) ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT SELECT @qx0_p1) THEN 1 ELSE 0 END AS [c1],
    CASE WHEN EXISTS (SELECT CASE WHEN (COUNT_BIG([T].[DocumentId]) > @qx0_p2) THEN COUNT_BIG([T].[DocumentId]) ELSE CAST(NULL AS decimal(38, 6)) END INTERSECT (SELECT @qx0_p0 UNION ALL SELECT @qx0_p3)) THEN 1 ELSE 0 END AS [c2]
FROM [gl].[Documents] AS [T]
INNER JOIN [gl].[Segments] AS [P1] ON [P1].[SegmentId] = [T].[SegmentId]
GROUP BY [P1].[SegmentName]
;

-- parameters
--   @qx0_p0 : QxNumeric QxDecimal(1, 0) Literal = 1
--   @qx0_p1 : QxNumeric QxDecimal(1, 0) Literal = 2
--   @qx0_p2 : QxNumeric QxDecimal(1, 0) Literal = 0
--   @qx0_p3 : QxNumeric QxDecimal(1, 0) Literal = 3
-- columns
--   0 : QxString NotNull key path=Centre.Name Centre.Name
--   1 : QxBool NotNull if(count() > 1, count(), null) = 2
--   2 : QxBool NotNull if(count() > 0, count(), null) in (1, 3)
