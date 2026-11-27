// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §10.4 (Parse): the whole workbook is read once, before any database call. The mapping's
    // statuses the import refuses become errors (§8.1); every mapped cell is decoded by its mapped
    // type (§3.1); the row key is settled (§9.2) and in-file uniqueness checked; child rows are linked
    // to their owner rows by Id at every depth and counted per owner (§9.5, §8.1); references to the
    // stack's own entity are matched against the parent sheet first (§10.2), and the parent rows put
    // in the order their self-references to new rows need, any cycle among them refused (§9.6). Every
    // error is collected, and any error refuses the file, so no lookup runs for a file that cannot be
    // saved. A synchronous import counts the rows it reads, never trusting a sheet's dimension, and
    // stops decoding once a limit is crossed, skimming the rest to report the true size (§2.3).
    /// <summary>Parses an import's workbook into the session its chunks resolve from.</summary>
    /// <param name="entity">The stack's entity.</param>
    /// <param name="limits">The stack's limits.</param>
    /// <param name="options">The codec's options.</param>
    /// <param name="context">The request's context.</param>
    /// <param name="decoders">What each mapped column decodes into.</param>
    internal sealed class ImportParser(EntityMetadata entity, StackLimits limits, ExcelOptions options, ExcelContext context, ImportDecoders decoders)
    {
        /// <summary>How many rows the parse reads between two checks of its cancellation.</summary>
        private const int CancellationStride = 1024;

        /// <summary>The stack's entity.</summary>
        private readonly EntityMetadata _entity = entity ?? throw new ArgumentNullException(nameof(entity));

        /// <summary>The stack's limits.</summary>
        private readonly StackLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));

        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>The request's context.</summary>
        private readonly ExcelContext _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>What each mapped column decodes into.</summary>
        private readonly ImportDecoders _decoders = decoders ?? throw new ArgumentNullException(nameof(decoders));

        /// <summary>The cycles left for resolution to refuse, once the rows' identities are known.</summary>
        private readonly List<ImportCycle> _cycles = [];

        /// <summary>The errors found so far.</summary>
        private readonly List<ImportError> _errors = [];

        /// <summary>The places and codes already reported, so two checks of one cell report it once.</summary>
        private readonly HashSet<(string? Sheet, int? Row, string? Column, string Code)> _raised = [];

        /// <summary>Parses a workbook.</summary>
        /// <param name="workbook">The opened workbook.</param>
        /// <param name="mapping">The workbook's mapping.</param>
        /// <param name="request">The import's request.</param>
        /// <param name="synchronous">Whether the import runs in the request, under the synchronous limits.</param>
        /// <param name="cancellationToken">Stops the parse between sheets and rows.</param>
        /// <returns>The session.</returns>
        /// <exception cref="BadRequestException">An update or upsert names no row key the file offers.</exception>
        /// <exception cref="ImportException">The workbook is above a synchronous limit, or holds an error.</exception>
        /// <exception cref="OperationCanceledException">The parse was cancelled.</exception>
        public ImportSession Parse(WorkbookReader workbook, ImportMapping mapping, ImportRequest request, bool synchronous, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            ArgumentNullException.ThrowIfNull(mapping);
            ArgumentNullException.ThrowIfNull(request);
            ImportPlan plan = mapping.Plan;
            bool sameSource = plan.SameSource;
            ImportMode mode = request.Mode;

            // --- The synchronous import's byte limits, which the package's sizes settle at once ---
            if (synchronous)
            {
                CheckBytes(workbook, plan.TotalRows);
            }

            // --- The statuses the import refuses, and the warnings it reports ---
            RefuseStatuses(mapping, request, sameSource);
            List<ImportError> warnings = Warnings(mapping, request, sameSource);

            // --- The sheets the import reads, each with its columns and its owner sheet ---
            bool useStamps = mode != ImportMode.Insert && sameSource && !request.IgnoreSheetStamps;
            List<ImportSheet> sheets = Sheets(mapping, useStamps);
            ImportSheet? parent = sheets.FirstOrDefault(static sheet => sheet.Collection is null);

            // --- The row key, before any row is read ---
            ImportColumn? keyColumn = RowKey(mode, request.RowKey ?? plan.DefaultRowKey, parent, sameSource, mapping.Manifest, out string? rowKey);

            // --- Every data row, decoded; then the links, the checks and the order ---
            ReadRows(workbook, sheets, synchronous, cancellationToken);
            CheckKeyLengths(sheets, parent, keyColumn);
            Link(sheets);
            CheckUniqueness(sheets, parent, mode == ImportMode.Insert ? null : keyColumn);
            CheckCounts(sheets, parent);
            MatchInSheet(sheets, parent);
            List<ImportRow> order = Order(parent, mode, rowKey);

            return _errors.Count > 0
                ? throw ImportMapper.Refusal(workbook.Sheets, _errors, _options.MaxReportedErrors)
                : new ImportSession(
                    new ImportSessionShape(_entity, _limits, _context, mode, rowKey, keyColumn, sameSource, useStamps),
                    parent,
                    sheets,
                    order,
                    warnings,
                    OrderHash(order, sheets),
                    workbook.Sheets,
                    _cycles);
        }

        /// <summary>Refuses a package above a synchronous byte limit, with the rows its dimensions declare.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="declaredRows">The rows the sheets' dimensions declare, reported beside the byte limit crossed.</param>
        /// <exception cref="ImportException">The package is above a byte limit.</exception>
        private void CheckBytes(WorkbookReader workbook, long declaredRows)
        {
            if (workbook.CompressedBytes > _options.MaxSynchronousImportFileBytes)
            {
                throw ImportFaults.TooLargeForSynchronous(declaredRows, _options.MaxSynchronousImportRows, workbook.CompressedBytes, _options.MaxSynchronousImportFileBytes);
            }

            if (workbook.UncompressedBytes > _options.MaxSynchronousImportUncompressedBytes)
            {
                throw ImportFaults.TooLargeForSynchronous(declaredRows, _options.MaxSynchronousImportRows, workbook.UncompressedBytes, _options.MaxSynchronousImportUncompressedBytes);
            }
        }

        /// <summary>
        ///     Raises the plan's statuses the import refuses: a duplicate sheet at each of its sheets,
        ///     and on each bound sheet an unmapped column (unless the request ignores them), a duplicate
        ///     column and a server-owned column; and a reference in its target's surrogate on a file of
        ///     another tenant that the caller did not map to it.
        /// </summary>
        /// <param name="mapping">The mapping.</param>
        /// <param name="request">The request.</param>
        /// <param name="sameSource">Whether the file is same-source.</param>
        private void RefuseStatuses(ImportMapping mapping, ImportRequest request, bool sameSource)
        {
            HashSet<(string Sheet, int Column)> overridden = new(
                request.ColumnMappings.Select(static mapping => (mapping.Sheet, ColumnLetters.TryParse(mapping.Column, out int column) ? column : -1)),
                ImportRequests.SheetColumnComparer.Instance);
            foreach (ImportMappedSheet sheet in mapping.Sheets)
            {
                string name = sheet.Sheet.Name;
                if (sheet.Status == ImportSheetStatus.Duplicate)
                {
                    Raise(name, row: null, column: null, header: null, property: null, ExcelErrorCodes.ImportDuplicateSheet, new() { ["collection"] = sheet.CollectionPath });
                    continue;
                }

                if (sheet.Status is not (ImportSheetStatus.Parent or ImportSheetStatus.Child))
                {
                    continue;
                }

                int row = Math.Max(1, sheet.HeaderRow);
                foreach (ImportMappedColumn column in sheet.Columns)
                {
                    switch (column.Status)
                    {
                        case ImportColumnStatus.Unmapped when !request.IgnoreUnmappedColumns:
                            Raise(name, row, column.Letter, column.Header, property: null, ExcelErrorCodes.ImportUnmappedColumn, new()
                            {
                                ["header"] = column.Header,
                                ["suggestions"] = column.Suggestions.ToArray(),
                            });
                            break;
                        case ImportColumnStatus.Duplicate:
                            Raise(name, row, column.Letter, column.Header, property: null, ExcelErrorCodes.ImportDuplicateColumn, new() { ["path"] = column.Path?.Text });
                            break;
                        case ImportColumnStatus.ServerOwned:
                            Raise(name, row, column.Letter, column.Header, property: null, ExcelErrorCodes.ImportServerOwnedColumn, new() { ["path"] = column.Path?.Text });
                            break;

                        // Spec 0019 §6.3: a surrogate names a row of the source tenant, so on another
                        // tenant's file it names nothing unless the caller mapped the column to it.
                        case ImportColumnStatus.Mapped when !sameSource
                            && mapping.Manifest is not null
                            && column.Path is { Kind: ImportPathKind.Reference, Reference: ReferenceMetadata reference, Key: PropertyMetadata key }
                            && key.Name == reference.Target.Key.Name
                            && !overridden.Contains((name, column.Index)):
                            Raise(name, row: null, column.Letter, column.Header, property: null, ExcelErrorCodes.ImportSurrogateReferenceFromOtherTenant, new() { ["entity"] = reference.Target.Name });
                            break;
                        case ImportColumnStatus.Unmapped:
                        case ImportColumnStatus.Mapped:
                        case ImportColumnStatus.Ignored:
                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>
        ///     The warnings the import reports: the plan's, without a stamp's when the request ignores
        ///     the sheet's stamps; and on a same-source file in insert mode, each mapped identity and
        ///     stamp column, which then only links rows or is ignored.
        /// </summary>
        /// <param name="mapping">The mapping.</param>
        /// <param name="request">The request.</param>
        /// <param name="sameSource">Whether the file is same-source.</param>
        /// <returns>The warnings.</returns>
        private static List<ImportError> Warnings(ImportMapping mapping, ImportRequest request, bool sameSource)
        {
            // A caller who ignores the sheet's stamps asked for it, so no stamp is reported (spec 0019 §8.1 step 7).
            List<ImportError> warnings = [.. mapping.Plan.Warnings.Where(warning => !(request.IgnoreSheetStamps
                && warning.Code == ExcelErrorCodes.ImportIdentityColumnIgnored
                && warning.Arguments.GetValueOrDefault("path") is ImportPaths.Stamp))];
            if (!sameSource || request.Mode != ImportMode.Insert)
            {
                return warnings;
            }

            foreach (ImportMappedSheet sheet in mapping.Sheets.Where(static sheet => sheet.Status is ImportSheetStatus.Parent or ImportSheetStatus.Child))
            {
                foreach (ImportMappedColumn column in sheet.Columns)
                {
                    if (column is { Status: ImportColumnStatus.Mapped, Path.Kind: ImportPathKind.Id or ImportPathKind.Stamp }
                        && !(request.IgnoreSheetStamps && column.Path.Kind == ImportPathKind.Stamp))
                    {
                        warnings.Add(new ImportError(sheet.Sheet.Name, Math.Max(1, sheet.HeaderRow), column.Letter, column.Header, Property: null, ExcelErrorCodes.ImportIdentityColumnIgnored, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["path"] = column.Path.Text,
                            ["reason"] = ImportSession.InsertModeReason,
                        }));
                    }
                }
            }

            return warnings;
        }

        /// <summary>
        ///     The sheets the import reads: the parent sheet and each child sheet, with the columns it
        ///     maps (the stamp only when it is the expected stamp), linked to their owner sheets; a
        ///     child sheet whose owner sheet is absent, or maps no identity, is refused.
        /// </summary>
        /// <param name="mapping">The mapping.</param>
        /// <param name="useStamps">Whether the sheet's stamps are the expected stamps.</param>
        /// <returns>The sheets, in tab order.</returns>
        private List<ImportSheet> Sheets(ImportMapping mapping, bool useStamps)
        {
            // --- The parent sheet first, so a reference to the stack's entity finds its key there ---
            List<ImportMappedSheet> bound = [.. mapping.Sheets.Where(static sheet => sheet.Status is ImportSheetStatus.Parent or ImportSheetStatus.Child && sheet.Entity is not null)];
            ImportSheet? parent = null;
            if (bound.FirstOrDefault(static sheet => sheet.Status == ImportSheetStatus.Parent) is ImportMappedSheet parentSheet)
            {
                parent = new ImportSheet(parentSheet, parentSheet.Entity!, Columns(parentSheet, useStamps, parent: null));
            }

            List<ImportSheet> sheets = [];
            foreach (ImportMappedSheet sheet in bound)
            {
                sheets.Add(sheet.Status == ImportSheetStatus.Parent ? parent! : new ImportSheet(sheet, sheet.Entity!, Columns(sheet, useStamps, parent)));
            }

            // --- Each child sheet's owner: the sheet of the collection declaring it, which must map its rows' identity ---
            foreach (ImportSheet sheet in sheets.Where(static sheet => sheet.Collection is not null))
            {
                ImportCollection collection = sheet.Collection!;
                ImportSheet? owner = sheets.FirstOrDefault(candidate => string.Equals(candidate.Path, collection.OwnerPath, StringComparison.Ordinal));
                if (owner?.Id is null)
                {
                    Raise(sheet.Name, row: null, column: null, header: null, property: null, ExcelErrorCodes.ImportOwnerSheetAbsent, new()
                    {
                        ["collection"] = collection.Path,
                        ["owner"] = collection.OwnerPath,
                    });
                    continue;
                }

                sheet.Owner = owner;
                owner.Children.Add(sheet);
            }

            // Each owner's child sheets in collection order, as the entity declares them.
            IReadOnlyList<ImportCollection> order = ImportPaths.Collections(_entity);
            foreach (ImportSheet sheet in sheets)
            {
                sheet.Children.Sort((x, y) => IndexOf(order, x.Path).CompareTo(IndexOf(order, y.Path)));
            }

            return sheets;

            static int IndexOf(IReadOnlyList<ImportCollection> collections, string path)
            {
                for (int i = 0; i < collections.Count; i++)
                {
                    if (string.Equals(collections[i].Path, path, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>The columns of a sheet the import reads: each mapped column, the stamp only when it is the expected stamp.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="useStamps">Whether the sheet's stamps are the expected stamps.</param>
        /// <param name="parent">The parent sheet, where a reference to the stack's entity finds its key; null while it is built, and for none.</param>
        /// <returns>The columns.</returns>
        private List<ImportColumn> Columns(ImportMappedSheet sheet, bool useStamps, ImportSheet? parent)
        {
            List<ImportColumn> columns = [];
            foreach (ImportMappedColumn column in sheet.Columns)
            {
                if (column is not { Status: ImportColumnStatus.Mapped, Path: ImportPath path } || (path.Kind == ImportPathKind.Stamp && !useStamps))
                {
                    continue;
                }

                // A reference to the stack's own entity may name a row of the parent sheet, by the parent sheet's column of its key.
                bool toStack = path is { Kind: ImportPathKind.Reference, Reference: ReferenceMetadata reference } && reference.Target.Name == _entity.Name;
                ImportColumn added = new(column, path, _decoders.For(sheet.Sheet.Name, sheet.Entity!, column, path), columns.Count)
                {
                    ToStackEntity = toStack,
                };
                columns.Add(added);
            }

            // --- The parent sheet's key column of each such reference: its own sheet's when it is the parent ---
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i] is { ToStackEntity: true, Path.Key: PropertyMetadata key })
                {
                    IReadOnlyList<ImportColumn> keyed = parent?.Columns ?? (sheet.Status == ImportSheetStatus.Parent ? columns : []);
                    ImportColumn? match = key.Name == _entity.Key.Name
                        ? keyed.FirstOrDefault(static candidate => candidate.Path.Kind == ImportPathKind.Id)
                        : keyed.FirstOrDefault(candidate => candidate.Path.Kind == ImportPathKind.Property && candidate.Path.Property.Name == key.Name);
                    columns[i] = columns[i] with { InSheetKey = match };
                }
            }

            return columns;
        }

        /// <summary>
        ///     Settles the row key: none for an insert; for an update or upsert the request's, else the
        ///     plan's default, else a request fault. <c>Id</c> keys a same-source file only.
        /// </summary>
        /// <param name="mode">The import's mode.</param>
        /// <param name="requested">The request's row key, else the plan's default; null for none.</param>
        /// <param name="parent">The parent sheet; null for none.</param>
        /// <param name="sameSource">Whether the file is same-source.</param>
        /// <param name="manifest">The workbook's manifest; null for none.</param>
        /// <param name="rowKey">The row key; null for an insert.</param>
        /// <returns>The parent sheet's column of the row key; for an insert, the identity column when it is mapped.</returns>
        /// <exception cref="BadRequestException">An update or upsert names no row key, or one the parent sheet does not map.</exception>
        private ImportColumn? RowKey(ImportMode mode, string? requested, ImportSheet? parent, bool sameSource, ImportManifest? manifest, out string? rowKey)
        {
            if (mode == ImportMode.Insert)
            {
                rowKey = null;
                return parent?.Id;
            }

            rowKey = requested ?? throw new BadRequestException(
                "An update or upsert needs a row key: name one, or import a file whose parent sheet maps its identity or a natural key.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = "rowKey" });

            // --- The identity: a key on a same-source file alone ---
            if (rowKey == ImportPaths.Id)
            {
                if (!sameSource)
                {
                    Raise(sheet: null, row: null, column: null, header: null, property: null, ExcelErrorCodes.ImportIdKeyRequiresSameTenant, new()
                    {
                        ["deployment"] = manifest?.Deployment,
                        ["sourceTenantId"] = manifest?.SourceTenantId,
                    });
                    return null;
                }

                return parent?.Id ?? throw Unmapped(rowKey);
            }

            // --- A natural key: the parent sheet's first column of it, any slot of a multilingual one ---
            string key = rowKey;
            return parent?.Columns.FirstOrDefault(column => column.Path.Kind == ImportPathKind.Property && Covers(column.Path.Property.Name, key)) ?? throw Unmapped(rowKey);

            static BadRequestException Unmapped(string key)
            {
                return new BadRequestException(
                    $"The row key {key} is not mapped by a column of the parent sheet.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = "rowKey", ["name"] = key });
            }
        }

        /// <summary>Whether a column writing a property covers a natural key: the key itself, or another slot of its multilingual group.</summary>
        /// <param name="property">The property the column writes.</param>
        /// <param name="key">The natural key's property.</param>
        /// <returns>True when it does.</returns>
        private bool Covers(string property, string key)
        {
            return string.Equals(property, key, StringComparison.Ordinal)
                || (ImportPaths.Slot(_entity, property) is (MultilingualGroup group, _) && string.Equals(group.Primary.Name, key, StringComparison.Ordinal));
        }

        /// <summary>
        ///     Reads every sheet's data rows, decoding each mapped cell; a fully blank row is none. A
        ///     synchronous import counts the rows as it reads, and past a limit skims the rest to report
        ///     the true size.
        /// </summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="sheets">The sheets.</param>
        /// <param name="synchronous">Whether the synchronous limits apply.</param>
        /// <param name="cancellationToken">Stops the read between rows.</param>
        /// <exception cref="ImportException">A synchronous limit is crossed.</exception>
        private void ReadRows(WorkbookReader workbook, List<ImportSheet> sheets, bool synchronous, CancellationToken cancellationToken)
        {
            CellDecodeContext decoding = new(workbook.Date1904, _context.Culture, _context.Calendar, _context.TimeZone);
            int ceiling = Math.Min(_limits.MaxSaveCount, _limits.MaxIds);
            long total = 0;
            long parents = 0;
            bool over = false;
            int read = 0;
            List<SheetCell> cells = [];
            foreach (ImportSheet sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var byIndex = sheet.Columns.ToDictionary(static column => column.Mapped.Index);
                using SheetReader reader = workbook.OpenSheet(sheet.Mapped.Sheet);
                while (true)
                {
                    if (++read % CancellationStride == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    // --- Past a limit, the rest is skimmed for its size alone ---
                    if (over)
                    {
                        if (!reader.Skim(out int skimmed, out bool filled))
                        {
                            break;
                        }

                        if (filled && skimmed > sheet.Mapped.HeaderRow)
                        {
                            total++;
                            parents += sheet.Collection is null ? 1 : 0;
                        }

                        continue;
                    }

                    if (!reader.Read(cells, out int row))
                    {
                        break;
                    }

                    // --- A data row: below the header row, with a cell of more than white space ---
                    if (row <= sheet.Mapped.HeaderRow || cells.TrueForAll(static cell => cell.Value.Kind == ExcelCellKind.Blank || string.IsNullOrWhiteSpace(CellCodec.AsText(cell.Value))))
                    {
                        continue;
                    }

                    total++;
                    parents += sheet.Collection is null ? 1 : 0;
                    if (synchronous && (total > _options.MaxSynchronousImportRows || total > _limits.MaxRowsPerSave || parents > ceiling))
                    {
                        over = true;
                        continue;
                    }

                    sheet.Rows.Add(Decode(sheet, row, cells, byIndex, decoding));
                }
            }

            if (!over)
            {
                return;
            }

            // --- The row limit crossed, by the true counts ---
            (long rows, long maxRows) = total > _options.MaxSynchronousImportRows ? (total, _options.MaxSynchronousImportRows)
                : parents > ceiling ? (parents, ceiling)
                : (total, (long)_limits.MaxRowsPerSave);
            throw ImportFaults.TooLargeForSynchronous(rows, maxRows, workbook.CompressedBytes, _options.MaxSynchronousImportFileBytes);
        }

        /// <summary>Decodes one data row's mapped cells, raising each cell that does not decode.</summary>
        /// <param name="sheet">The row's sheet.</param>
        /// <param name="row">The Excel row.</param>
        /// <param name="cells">The row's stored cells.</param>
        /// <param name="byIndex">The sheet's columns, by column index.</param>
        /// <param name="decoding">The workbook's and the request's decoding facts.</param>
        /// <returns>The row.</returns>
        private ImportRow Decode(ImportSheet sheet, int row, List<SheetCell> cells, Dictionary<int, ImportColumn> byIndex, CellDecodeContext decoding)
        {
            object?[] values = new object?[sheet.Columns.Count];
            foreach (SheetCell cell in cells)
            {
                if (!byIndex.TryGetValue(cell.Column, out ImportColumn? column))
                {
                    continue;
                }

                CellDecodeResult result = CellCodec.Decode(cell.Value, column.Target, decoding);
                if (!result.Success)
                {
                    Raise(sheet.Name, row, column, ExcelErrorCodes.ImportInvalidCell, new(result.ErrorArguments()));
                    continue;
                }

                // --- An identity names a row only within its key type's range ---
                if (result.Value is long number && IdentityType(sheet, column) is Type keyType && !ImportKeys.Fits(keyType, number))
                {
                    Raise(sheet.Name, row, column, ExcelErrorCodes.ImportInvalidCell, new()
                    {
                        ["expected"] = ExpectedValues.Integer,
                        ["value"] = CellDecodeResult.Refused(ExpectedValues.Integer, CellCodec.AsText(cell.Value)).Text,
                    });
                    continue;
                }

                values[column.Slot] = result.Value;
            }

            return new ImportRow(sheet, row, values);
        }

        /// <summary>The key type an identity column names a row of: the sheet's own, the owner's, or a reference's target's.</summary>
        /// <param name="sheet">The column's sheet.</param>
        /// <param name="column">The column.</param>
        /// <returns>The key's CLR type; null for a column that is no identity.</returns>
        private static Type? IdentityType(ImportSheet sheet, ImportColumn column)
        {
            return column.Path.Kind switch
            {
                ImportPathKind.Id => sheet.Entity.Key.ClrType,
                ImportPathKind.ParentReference => column.Path.Property.ClrType,
                ImportPathKind.Reference when column.Path.Reference is ReferenceMetadata reference && column.Path.Key?.Name == reference.Target.Key.Name => reference.Target.Key.ClrType,
                ImportPathKind.Stamp or ImportPathKind.Property or ImportPathKind.Reference or ImportPathKind.Navigation => null,
                _ => null,
            };
        }

        /// <summary>
        ///     Refuses each text key longer than a lookup's string list holds: a reference cell, and the
        ///     natural row key's cell. The refused value is dropped, so nothing looks it up.
        /// </summary>
        /// <param name="sheets">The sheets.</param>
        /// <param name="parent">The parent sheet; null for none.</param>
        /// <param name="keyColumn">The row key's column; null for none.</param>
        private void CheckKeyLengths(List<ImportSheet> sheets, ImportSheet? parent, ImportColumn? keyColumn)
        {
            foreach (ImportSheet sheet in sheets)
            {
                ImportColumn[] keyed = [.. sheet.Columns.Where(column => column.Path.Kind == ImportPathKind.Reference || (ReferenceEquals(sheet, parent) && ReferenceEquals(column, keyColumn)))];
                foreach (ImportRow row in sheet.Rows)
                {
                    foreach (ImportColumn column in keyed)
                    {
                        if (row.Values[column.Slot] is string { Length: > ImportKeys.MaxTextLength })
                        {
                            Raise(sheet.Name, row.Row, column, ExcelErrorCodes.ImportKeyTooLong, new() { ["max"] = ImportKeys.MaxTextLength });
                            row.Values[column.Slot] = null;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Links each child row to the owner row whose identity its parent reference names, at every
        ///     depth; an owner sheet's identities must be unique, and a row naming no owner row is an orphan.
        /// </summary>
        /// <param name="sheets">The sheets.</param>
        private void Link(List<ImportSheet> sheets)
        {
            // Owners first: a collection's sheet is linked after its owner's, by depth.
            foreach (ImportSheet sheet in sheets.Where(static sheet => sheet.Owner is not null).OrderBy(static sheet => sheet.Path.Count(static c => c == '.')))
            {
                ImportSheet owner = sheet.Owner!;
                Dictionary<object, ImportRow> owners = new(ImportKeys.Comparer);
                foreach (ImportRow row in owner.Rows)
                {
                    if (row.Id is long id)
                    {
                        _ = owners.TryAdd(id, row);
                    }
                }

                // --- Each row to the owner row it names; an orphan's own rows raise nothing further ---
                ImportColumn? link = sheet.ParentLink;
                foreach (ImportRow row in sheet.Rows)
                {
                    object? value = link is null ? null : row[link];
                    if (value is long id && owners.TryGetValue(id, out ImportRow? ownerRow))
                    {
                        ownerRow.Adopt(row);
                    }
                    else
                    {
                        Raise(sheet.Name, row.Row, link, ExcelErrorCodes.ImportOrphanChildRow, new() { ["value"] = ImportKeys.Shown(value) });
                    }
                }
            }
        }

        /// <summary>
        ///     Refuses duplicate values, after normalisation, at every duplicate row: of the row key and
        ///     of each unique column of the parent sheet, and of the identity of every sheet owning a
        ///     present child sheet.
        /// </summary>
        /// <param name="sheets">The sheets.</param>
        /// <param name="parent">The parent sheet; null for none.</param>
        /// <param name="keyColumn">The row key's column of an update or upsert; null for none.</param>
        private void CheckUniqueness(List<ImportSheet> sheets, ImportSheet? parent, ImportColumn? keyColumn)
        {
            List<(ImportSheet Sheet, ImportColumn Column)> checks = [];
            if (parent is not null)
            {
                if (keyColumn is not null)
                {
                    checks.Add((parent, keyColumn));
                }

                checks.AddRange(parent.Columns.Where(static column => column.Path.Kind == ImportPathKind.Property && column.Path.Property.IsUnique).Select(column => (parent, column)));
            }

            checks.AddRange(sheets.Where(static sheet => sheet.Children.Count > 0 && sheet.Id is not null).Select(static sheet => (sheet, sheet.Id!)));
            foreach ((ImportSheet sheet, ImportColumn column) in checks)
            {
                foreach (IGrouping<object, ImportRow> group in sheet.Rows
                    .Select(row => (Row: row, Key: ImportKeys.Normalize(row[column])))
                    .Where(static entry => entry.Key is not null)
                    .GroupBy(static entry => entry.Key!, static entry => entry.Row, ImportKeys.Comparer))
                {
                    if (group.Skip(1).Any())
                    {
                        foreach (ImportRow row in group)
                        {
                            Raise(sheet.Name, row.Row, column, ExcelErrorCodes.ImportDuplicateRowKey, new() { ["value"] = ImportKeys.Shown(row[column]) });
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Refuses an owner row with more rows in a collection than the collection holds, at its
        ///     first row beyond the cap, and a parent row whose rows at every depth exceed one save.
        /// </summary>
        /// <param name="sheets">The sheets.</param>
        /// <param name="parent">The parent sheet; null for none.</param>
        private void CheckCounts(List<ImportSheet> sheets, ImportSheet? parent)
        {
            foreach (ImportSheet sheet in sheets.Where(static sheet => sheet.Owner is not null))
            {
                int max = sheet.Collection!.Collection.MaxCount;
                foreach (ImportRow owner in sheet.Owner!.Rows)
                {
                    IReadOnlyList<ImportRow> rows = owner.ChildrenOn(sheet);
                    if (max > 0 && rows.Count > max)
                    {
                        Raise(sheet.Name, rows[max].Row, sheet.ParentLink, ExcelErrorCodes.ImportTooManyChildren, new()
                        {
                            ["collection"] = sheet.Path,
                            ["max"] = max,
                            ["count"] = rows.Count,
                        });
                    }
                }
            }

            foreach (ImportRow row in parent?.Rows ?? [])
            {
                int rows = 1 + row.Descendants().Count();
                if (rows > _limits.MaxRowsPerSave)
                {
                    Raise(parent!.Name, row.Row, column: null, header: null, property: null, ExcelErrorCodes.ImportTooManyDescendants, new()
                    {
                        ["rows"] = rows,
                        ["max"] = _limits.MaxRowsPerSave,
                    });
                }
            }
        }

        /// <summary>
        ///     Matches each reference to the stack's entity against the parent sheet's column of its
        ///     key: a value naming one parent row binds to it within the workbook; one naming several is
        ///     ambiguous.
        /// </summary>
        /// <param name="sheets">The sheets.</param>
        /// <param name="parent">The parent sheet; null for none.</param>
        private void MatchInSheet(List<ImportSheet> sheets, ImportSheet? parent)
        {
            if (parent is null)
            {
                return;
            }

            Dictionary<ImportColumn, Dictionary<object, List<ImportRow>>> indexes = [];
            foreach (ImportSheet sheet in sheets)
            {
                foreach (ImportColumn column in sheet.Columns.Where(static column => column.ToStackEntity && column.InSheetKey is not null))
                {
                    // --- The parent sheet's rows by the key, built once per key column ---
                    ImportColumn key = column.InSheetKey!;
                    if (!indexes.TryGetValue(key, out Dictionary<object, List<ImportRow>>? index))
                    {
                        index = new Dictionary<object, List<ImportRow>>(ImportKeys.Comparer);
                        foreach (ImportRow row in parent.Rows)
                        {
                            if (ImportKeys.Normalize(row[key]) is object value)
                            {
                                if (!index.TryGetValue(value, out List<ImportRow>? rows))
                                {
                                    rows = [];
                                    index[value] = rows;
                                }

                                rows.Add(row);
                            }
                        }

                        indexes[key] = index;
                    }

                    foreach (ImportRow row in sheet.Rows)
                    {
                        if (ImportKeys.Normalize(row[column]) is not object value || !index.TryGetValue(value, out List<ImportRow>? matches))
                        {
                            continue;
                        }

                        if (matches.Count == 1)
                        {
                            row.InSheetTargets ??= [];
                            row.InSheetTargets[column.Slot] = matches[0];
                        }
                        else
                        {
                            Raise(sheet.Name, row.Row, column, ExcelErrorCodes.ImportReferenceAmbiguous, new()
                            {
                                ["entity"] = _entity.Name,
                                ["key"] = column.Path.Key!.Name,
                                ["value"] = ImportKeys.Shown(row[column]),
                                ["count"] = matches.Count,
                            });
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     The parent rows in the order their references to the stack's entity need: a row naming a
        ///     new row of the parent sheet, itself or through a row under it, comes after it, ready rows
        ///     taken in ascending Excel row. Rows on a cycle are refused; the rows no order reaches
        ///     follow in Excel order.
        /// </summary>
        /// <param name="parent">The parent sheet; null for none.</param>
        /// <param name="mode">The import's mode.</param>
        /// <param name="rowKey">The row key; null for an insert.</param>
        /// <returns>The parent rows, each given its position.</returns>
        private List<ImportRow> Order(ImportSheet? parent, ImportMode mode, string? rowKey)
        {
            List<ImportRow> rows = parent?.Rows ?? [];
            int count = rows.Count;
            Dictionary<ImportRow, int> indexOf = new(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < count; i++)
            {
                indexOf[rows[i]] = i;
            }

            // --- The edges: from each new row a reference names, to the parent row whose family names it ---
            // Spec 0019 §9.6 orders the parent sheet's self-references. A child row's reference to the
            // stack's entity is ordered too, from the named row to the child's parent row, so a chunked
            // import commits the named row no later than the chunk holding the child: a later chunk
            // could not bind it, neither through the committed ids nor by lookup. A child naming its
            // own parent row saves with it and adds no edge.
            var needs = new List<OrderEdge>[count];
            var dependents = new List<int>[count];
            int[] pending = new int[count];
            for (int i = 0; i < count; i++)
            {
                needs[i] = [];
                dependents[i] = [];
            }

            for (int i = 0; i < count; i++)
            {
                HashSet<int> named = [];
                foreach (ImportRow referrer in Family(rows[i]))
                {
                    foreach ((int slot, ImportRow target) in referrer.InSheetTargets ?? [])
                    {
                        int t = indexOf[target];
                        if ((t == i && !ReferenceEquals(referrer, rows[i])) || !PotentiallyNew(target, mode, rowKey) || !named.Add(t))
                        {
                            continue;
                        }

                        needs[i].Add(new OrderEdge(t, referrer.Sheet.Columns[slot], referrer));
                        dependents[t].Add(i);
                        pending[i]++;
                    }
                }
            }

            // --- Kahn's algorithm, the ready row of the lowest Excel row first ---
            PriorityQueue<int, int> ready = new();
            for (int i = 0; i < count; i++)
            {
                if (pending[i] == 0)
                {
                    ready.Enqueue(i, rows[i].Row);
                }
            }

            List<ImportRow> order = new(count);
            bool[] placed = new bool[count];
            while (ready.TryDequeue(out int next, out _))
            {
                placed[next] = true;
                order.Add(rows[next]);
                foreach (int dependent in dependents[next])
                {
                    if (--pending[dependent] == 0)
                    {
                        ready.Enqueue(dependent, rows[dependent].Row);
                    }
                }
            }

            // --- The rows left over: those on a cycle refused, every one placed after the rest ---
            // Spec 0019 §9.6 refuses a cycle among new rows. An upsert by a natural key cannot tell
            // which rows are new before its lookups, so its cycles wait for resolution.
            if (order.Count < count)
            {
                RefuseCycles(rows, needs, placed, deferred: mode == ImportMode.Upsert && rowKey != ImportPaths.Id);
                order.AddRange(rows.Where((_, i) => !placed[i]));
            }

            for (int position = 0; position < order.Count; position++)
            {
                order[position].Position = position;
            }

            return order;
        }

        /// <summary>
        ///     Whether a parent row may be new, so a row naming it must follow it: every row of an
        ///     insert, none of an update, and in an upsert by identity a row of a blank or negative
        ///     identity. An upsert by a natural key cannot tell before its lookups, so every row may be.
        /// </summary>
        /// <param name="row">The parent row.</param>
        /// <param name="mode">The import's mode.</param>
        /// <param name="rowKey">The row key; null for an insert.</param>
        /// <returns>True when the row may be new.</returns>
        private static bool PotentiallyNew(ImportRow row, ImportMode mode, string? rowKey)
        {
            return mode switch
            {
                ImportMode.Insert => true,
                ImportMode.Update => false,
                ImportMode.Upsert => rowKey != ImportPaths.Id || row.Id is not > 0,
                _ => true,
            };
        }

        /// <summary>
        ///     Refuses each row on a cycle of references among the rows no order reached: every
        ///     strongly connected group of more than one row, or a row naming itself, at the cell
        ///     naming another row of its cycle. A deferred cycle is kept for resolution instead.
        /// </summary>
        /// <param name="rows">The parent rows, in Excel order.</param>
        /// <param name="needs">Each row's edges: the rows its family names that may be new, with the cell naming each.</param>
        /// <param name="placed">Whether each row was placed.</param>
        /// <param name="deferred">Whether the rows' newness is unknown until resolution, which then refuses the cycle.</param>
        private void RefuseCycles(List<ImportRow> rows, List<OrderEdge>[] needs, bool[] placed, bool deferred)
        {
            foreach (List<int> group in StronglyConnected(needs, placed))
            {
                HashSet<int> members = [.. group];
                if (group.Count == 1 && !needs[group[0]].Any(need => need.Target == group[0]))
                {
                    continue;
                }

                int[] ordered = [.. group.OrderBy(index => rows[index].Row)];
                OrderEdge[] edges = [.. ordered.Select(index => needs[index].First(need => members.Contains(need.Target)))];
                if (deferred)
                {
                    _cycles.Add(new ImportCycle([.. ordered.Select(index => rows[index])], [.. edges.Select(static edge => edge.Column)], [.. edges.Select(static edge => edge.Referrer)]));
                    continue;
                }

                long[] cycleRows = [.. ordered.Select(index => (long)rows[index].Row)];
                foreach (OrderEdge edge in edges)
                {
                    Raise(edge.Referrer.Sheet.Name, edge.Referrer.Row, edge.Column, ExcelErrorCodes.ImportParentCycle, new()
                    {
                        ["rows"] = cycleRows,
                        ["path"] = edge.Column.Path.Text,
                    });
                }
            }
        }

        /// <summary>The strongly connected groups of the unplaced rows' edges, by Tarjan's algorithm, iteratively.</summary>
        /// <param name="needs">Each row's edges.</param>
        /// <param name="placed">Whether each row was placed; placed rows are no part of a group.</param>
        /// <returns>The groups, each a list of row indexes.</returns>
        private static List<List<int>> StronglyConnected(List<OrderEdge>[] needs, bool[] placed)
        {
            int count = needs.Length;
            int[] index = new int[count];
            int[] low = new int[count];
            bool[] onStack = new bool[count];
            Array.Fill(index, -1);
            Stack<int> stack = new();
            List<List<int>> groups = [];
            int counter = 0;

            for (int root = 0; root < count; root++)
            {
                if (placed[root] || index[root] >= 0)
                {
                    continue;
                }

                // --- A depth-first walk with an explicit stack of (row, next edge) frames ---
                Stack<(int Row, int Edge)> walk = new();
                walk.Push((root, 0));
                index[root] = low[root] = counter++;
                stack.Push(root);
                onStack[root] = true;
                while (walk.Count > 0)
                {
                    (int row, int edge) = walk.Pop();
                    if (edge < needs[row].Count)
                    {
                        walk.Push((row, edge + 1));
                        int target = needs[row][edge].Target;
                        if (placed[target])
                        {
                            continue;
                        }

                        if (index[target] < 0)
                        {
                            index[target] = low[target] = counter++;
                            stack.Push(target);
                            onStack[target] = true;
                            walk.Push((target, 0));
                        }
                        else if (onStack[target])
                        {
                            low[row] = Math.Min(low[row], index[target]);
                        }

                        continue;
                    }

                    // --- The row's edges are done: close its group when it is the group's root ---
                    if (walk.Count > 0)
                    {
                        int caller = walk.Peek().Row;
                        low[caller] = Math.Min(low[caller], low[row]);
                    }

                    if (low[row] == index[row])
                    {
                        List<int> group = [];
                        int member;
                        do
                        {
                            member = stack.Pop();
                            onStack[member] = false;
                            group.Add(member);
                        }
                        while (member != row);
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }

        /// <summary>
        ///     The order's fingerprint: the Excel row of each position, and every column the import
        ///     reads with its sheet, letters, path and language, so a resumed import tells a changed
        ///     file or mapping.
        /// </summary>
        /// <param name="order">The parent rows in order.</param>
        /// <param name="sheets">The sheets.</param>
        /// <returns>The SHA-256 hash, as lowercase hexadecimal.</returns>
        private static string OrderHash(List<ImportRow> order, List<ImportSheet> sheets)
        {
            StringBuilder text = new();
            _ = text.Append("rows:");
            foreach (ImportRow row in order)
            {
                _ = text.Append(row.Row.ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            foreach (ImportSheet sheet in sheets)
            {
                foreach (ImportColumn column in sheet.Columns)
                {
                    _ = text.Append('\u001F').Append(sheet.Name).Append('\u001E').Append(column.Letter).Append('\u001E').Append(column.Path.Text).Append('\u001E').Append(column.Path.Language);
                }
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        /// <summary>Raises an error at a row's cell.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="row">The row.</param>
        /// <param name="column">The column; null for the row alone.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        private void Raise(string sheet, int row, ImportColumn? column, string code, Dictionary<string, object?> arguments)
        {
            Raise(sheet, row, column?.Letter, column?.Header, column?.Path.Property.Name, code, arguments);
        }

        /// <summary>Raises an error once per place and code.</summary>
        /// <param name="sheet">The sheet; null for the workbook.</param>
        /// <param name="row">The row; null for none.</param>
        /// <param name="column">The column's letters; null for none.</param>
        /// <param name="header">The column's header; null for none.</param>
        /// <param name="property">The property the column writes; null for none.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        private void Raise(string? sheet, int? row, string? column, string? header, string? property, string code, Dictionary<string, object?> arguments)
        {
            if (_raised.Add((sheet, row, column, code)))
            {
                _errors.Add(new ImportError(sheet, row, column, header, property, code, new Dictionary<string, object?>(arguments, StringComparer.Ordinal)));
            }
        }

        /// <summary>A parent row and every row under it, depth first.</summary>
        /// <param name="row">The parent row.</param>
        /// <returns>The rows.</returns>
        private static IEnumerable<ImportRow> Family(ImportRow row)
        {
            yield return row;
            foreach (ImportRow descendant in row.Descendants())
            {
                yield return descendant;
            }
        }

        /// <summary>An edge of the order: the parent row a reference names, the column naming it, and the row whose cell it is.</summary>
        /// <param name="Target">The index of the parent row named.</param>
        /// <param name="Column">The reference column.</param>
        /// <param name="Referrer">The row whose cell names it: the parent row, or a row under it.</param>
        private readonly record struct OrderEdge(int Target, ImportColumn Column, ImportRow Referrer);
    }
}
