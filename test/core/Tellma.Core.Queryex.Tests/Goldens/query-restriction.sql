SELECT
    [T].[DocumentId] AS [c0],
    [T].[DocCode] AS [c1]
FROM [gl].[Documents] AS [T]
WHERE ([T].[DocumentId] IN (SELECT [Id] FROM @tb0_invoices))
;

-- parameters
-- columns
--   0 : QxNumeric NotNull path=Id Id
--   1 : QxString NotNull path=Code Code
