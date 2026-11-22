// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §8.1: the mapping reads the manifest and each bound sheet's header row and dimension,
    // never a data row beyond what a sheet without a dimension needs counted. It binds every sheet,
    // resolves every header cell, and reports what it found as the plan; a sheet, column or path the
    // overrides name and the workbook or entity lacks is an error, thrown together once every sheet
    // is mapped. Unmapped, duplicate and server-owned columns, and duplicate sheets, are statuses of
    // the plan; the import refuses them itself.
    /// <summary>Maps a workbook's sheets and columns to a stack's entity, producing the import plan.</summary>
    /// <param name="entity">The stack's entity.</param>
    /// <param name="limits">The stack's limits, whose save sizes bound a synchronous and an atomic import.</param>
    /// <param name="options">The codec's options.</param>
    /// <param name="context">The request's context: the target tenant, its languages, the culture and the labels.</param>
    /// <param name="cache">The header and sheet-name indexes.</param>
    internal sealed class ImportMapper(EntityMetadata entity, StackLimits limits, ExcelOptions options, ExcelContext context, HeaderIndexCache cache)
    {
        /// <summary>The most unmapped columns of one workbook given nearest-key suggestions; the rest get only colliding paths.</summary>
        public const int MaxSuggestedColumns = 50;

        /// <summary>The most suggestions an unmapped column gets.</summary>
        public const int MaxSuggestions = 3;

        /// <summary>The reason of an identity column ignored on a file from another deployment or tenant.</summary>
        public const string ForeignSourceReason = "ForeignSource";

        /// <summary>The reason of an identity column ignored on a file without a manifest.</summary>
        public const string NoManifestReason = "NoManifest";

        /// <summary>
        ///     The longest header the mapping matches, in characters, as no export writes a longer one.
        ///     A longer header is kept cut to this length and is unmapped unless an override maps it.
        /// </summary>
        public const int MaxHeaderLength = 255;

        /// <summary>How many rows the mapping reads between two checks of its cancellation.</summary>
        private const int CancellationStride = 1024;

        /// <summary>The stack's entity.</summary>
        private readonly EntityMetadata _entity = entity ?? throw new ArgumentNullException(nameof(entity));

        /// <summary>The stack's limits.</summary>
        private readonly StackLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));

        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>The request's context.</summary>
        private readonly ExcelContext _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>The indexes.</summary>
        private readonly HeaderIndexCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        /// <summary>The errors found so far.</summary>
        private readonly List<ImportError> _errors = [];

        /// <summary>The warnings found so far.</summary>
        private readonly List<ImportError> _warnings = [];

        /// <summary>The unmapped columns given nearest-key suggestions so far.</summary>
        private int _suggested;

        /// <summary>Maps a workbook.</summary>
        /// <param name="workbook">The opened workbook.</param>
        /// <param name="sheetMappings">The sheet overrides, already checked against the entity.</param>
        /// <param name="columnMappings">The column overrides, already checked for form.</param>
        /// <param name="cancellationToken">Stops the mapping between sheets and between rows.</param>
        /// <returns>The mapping.</returns>
        /// <exception cref="ImportException">The manifest names another entity, an override names a sheet, column or path the workbook or entity lacks, or a part read is malformed.</exception>
        /// <exception cref="OperationCanceledException">The mapping was cancelled.</exception>
        public ImportMapping Map(
            WorkbookReader workbook,
            IReadOnlyList<ImportSheetMapping> sheetMappings,
            IReadOnlyList<ImportColumnMapping> columnMappings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            ArgumentNullException.ThrowIfNull(sheetMappings);
            ArgumentNullException.ThrowIfNull(columnMappings);

            // --- The manifest, read and checked for its entity before anything is bound ---
            ImportManifest? manifest = ReadManifest(workbook, cancellationToken);
            bool sameSource = ExcelManifest.IsSameSource(manifest?.Header, _context.DeploymentId, _context.TenantId);

            // --- The overrides that name a sheet the workbook lacks ---
            HashSet<string> present = new(workbook.Sheets.Select(static sheet => sheet.Name), StringComparer.OrdinalIgnoreCase);
            HashSet<string> unknown = new(StringComparer.OrdinalIgnoreCase);
            foreach (string sheet in sheetMappings.Select(static mapping => mapping.Sheet).Concat(columnMappings.Select(static mapping => mapping.Sheet)))
            {
                if (!present.Contains(sheet) && unknown.Add(sheet))
                {
                    _errors.Add(Error(sheet, row: null, column: null, header: null, ExcelErrorCodes.ImportUnknownSheet, []));
                }
            }

            // --- Each sheet's binding, the fallback parent, and the duplicates ---
            IReadOnlyList<ImportCollection> collections = ImportPaths.Collections(_entity);
            List<Binding> bindings = Bind(workbook.Sheets, manifest, sheetMappings, collections);

            // --- Each bound sheet's header row and rows; each column resolved ---
            var overrides = columnMappings
                .GroupBy(static mapping => mapping.Sheet, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            List<ImportMappedSheet> sheets = [];
            foreach (Binding binding in bindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<ImportColumnMapping> own = overrides.GetValueOrDefault(binding.Sheet.Name) ?? [];
                sheets.Add(binding.Entity is EntityMetadata sheetEntity
                    ? MapSheet(workbook, binding, sheetEntity, manifest, own, cancellationToken)
                    : Unbound(binding, own));
            }

            // --- The identity columns of a file that is not same-source ---
            if (!sameSource)
            {
                sheets = [.. sheets.Select(sheet => GateIdentity(sheet, sheets, manifest))];
            }

            return _errors.Count > 0
                ? throw Refusal(workbook.Sheets, _errors)
                : new ImportMapping(Plan(workbook, sheets, manifest, sameSource), sheets, manifest);
        }

        /// <summary>The sorted, capped exception of errors.</summary>
        /// <param name="sheets">The workbook's sheets, whose tab order sorts the errors.</param>
        /// <param name="errors">The errors.</param>
        /// <param name="max">The most errors reported.</param>
        /// <returns>The exception.</returns>
        internal static ImportException Refusal(IReadOnlyList<WorkbookSheet> sheets, IReadOnlyList<ImportError> errors, int max)
        {
            Dictionary<string, int> positions = new(StringComparer.OrdinalIgnoreCase);
            foreach (WorkbookSheet sheet in sheets)
            {
                _ = positions.TryAdd(sheet.Name, sheet.Position);
            }

            // A fault of no sheet comes first, then a sheet the workbook lacks, then the workbook's in tab order.
            List<ImportError> sorted = [.. errors
                .OrderBy(error => error.Sheet is null ? -2 : positions.GetValueOrDefault(error.Sheet, -1))
                .ThenBy(static error => error.Sheet, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static error => error.Row ?? 0)
                .ThenBy(static error => error.Column is string letter && ColumnLetters.TryParse(letter, out int index) ? index : -1)];
            return new ImportException([.. sorted.Take(Math.Max(1, max))], sorted.Count, []);
        }

        /// <summary>The sorted, capped exception of this mapping's errors.</summary>
        /// <param name="sheets">The workbook's sheets.</param>
        /// <param name="errors">The errors.</param>
        /// <returns>The exception.</returns>
        private ImportException Refusal(IReadOnlyList<WorkbookSheet> sheets, IReadOnlyList<ImportError> errors)
        {
            return Refusal(sheets, errors, _options.MaxReportedErrors);
        }

        /// <summary>Reads the workbook's manifest, refusing one that names another entity.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="cancellationToken">Stops the read between rows.</param>
        /// <returns>The manifest; null when the workbook has no manifest sheet, or one this codec does not read.</returns>
        /// <exception cref="ImportException">The manifest names another entity, or none.</exception>
        private ImportManifest? ReadManifest(WorkbookReader workbook, CancellationToken cancellationToken)
        {
            if (workbook.Sheets.FirstOrDefault(static sheet => IsManifestSheet(sheet) && sheet.Part is not null) is not WorkbookSheet sheet)
            {
                return null;
            }

            ImportManifest? manifest;
            using (SheetReader reader = workbook.OpenSheet(sheet))
            {
                manifest = ImportManifest.Read(reader, cancellationToken);
            }

            return manifest is not null && !string.Equals(manifest.Entity, _entity.Name, StringComparison.Ordinal)
                ? throw ImportFaults.Malformed(ImportFaults.EntityReason, sheet.Name)
                : manifest;
        }

        /// <summary>Whether a sheet is the manifest's, by its reserved name.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <returns>True when it is.</returns>
        private static bool IsManifestSheet(WorkbookSheet sheet)
        {
            return string.Equals(sheet.Name, SheetNames.Manifest, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Binds each sheet to the parent entity, a collection path or nothing: an override wins,
        ///     then the manifest's sheet table, then the sheet's name; a very hidden sheet binds only
        ///     by an override. Without a manifest and with no sheet bound to the parent, the first
        ///     visible sheet bound to nothing is the parent's. Two sheets bound to one target are both
        ///     duplicates.
        /// </summary>
        /// <param name="workbookSheets">The workbook's sheets.</param>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <param name="sheetMappings">The sheet overrides.</param>
        /// <param name="collections">The entity's collection paths.</param>
        /// <returns>Each sheet's binding, in tab order.</returns>
        private List<Binding> Bind(
            IReadOnlyList<WorkbookSheet> workbookSheets,
            ImportManifest? manifest,
            IReadOnlyList<ImportSheetMapping> sheetMappings,
            IReadOnlyList<ImportCollection> collections)
        {
            var byPath = collections.ToDictionary(static collection => collection.Path, StringComparer.Ordinal);
            var mapped = sheetMappings.ToDictionary(static mapping => mapping.Sheet, StringComparer.OrdinalIgnoreCase);

            // The manifest's sheet table: each sheet's collection as its column table records it, when the entity has it.
            Dictionary<string, string> recorded = new(StringComparer.OrdinalIgnoreCase);
            foreach (ImportManifestColumn column in manifest?.Columns ?? [])
            {
                if (column.Collection.Length == 0 || byPath.ContainsKey(column.Collection))
                {
                    _ = recorded.TryAdd(column.Sheet, column.Collection);
                }
            }

            SheetIndex names = _cache.Sheets(_entity, collections, _context);
            List<Binding> bindings = [];
            foreach (WorkbookSheet sheet in workbookSheets)
            {
                // --- The manifest's own sheet, and a sheet without cells, which nothing binds ---
                mapped.TryGetValue(sheet.Name, out ImportSheetMapping? mapping);
                if (IsManifestSheet(sheet) || sheet.Part is null)
                {
                    if (mapping?.Collection is not null)
                    {
                        _errors.Add(Error(sheet.Name, row: null, column: null, header: null, ExcelErrorCodes.ImportUnknownSheet, []));
                    }

                    bindings.Add(new Binding(sheet, IsManifestSheet(sheet) ? ImportSheetStatus.Manifest : ImportSheetStatus.Ignored, Target: null, Matched: true));
                    continue;
                }

                // --- An override, the manifest, then the name; a very hidden sheet by an override alone ---
                string? target;
                bool matched = true;
                if (mapping is not null)
                {
                    target = mapping.Collection;
                }
                else if (sheet.State == WorkbookSheetState.VeryHidden)
                {
                    target = null;
                }
                else if (recorded.TryGetValue(sheet.Name, out string? collection))
                {
                    target = collection;
                }
                else if (names.TryMatch(sheet.Name, out string named))
                {
                    target = named;
                }
                else
                {
                    target = null;
                    matched = false;
                }

                bindings.Add(new Binding(sheet, target is null ? ImportSheetStatus.Ignored : Status(target), target, matched));
            }

            // --- The fallback parent: the first visible sheet that matched nothing ---
            if (manifest is null && !bindings.Any(static binding => binding.Target is { Length: 0 }))
            {
                int fallback = bindings.FindIndex(static binding => !binding.Matched && binding.Sheet.State == WorkbookSheetState.Visible);
                if (fallback >= 0)
                {
                    bindings[fallback] = bindings[fallback] with { Status = ImportSheetStatus.Parent, Target = string.Empty, Matched = true };
                }
            }

            // --- Two sheets bound to one target are both duplicates, never a first match ---
            foreach (IGrouping<string, Binding> group in bindings.Where(static binding => binding.Target is not null).GroupBy(static binding => binding.Target!, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                {
                    foreach (Binding duplicate in group)
                    {
                        int index = bindings.IndexOf(duplicate);
                        bindings[index] = duplicate with { Status = ImportSheetStatus.Duplicate };
                    }
                }
            }

            // --- Each bound sheet's entity: the stack's for the parent, the collection's child for a collection ---
            return [.. bindings.Select(binding => binding.Target switch
            {
                null => binding,
                { Length: 0 } => binding with { Entity = _entity },
                string path => binding with { Collection = byPath[path], Entity = byPath[path].Collection.Child },
            })];
        }

        /// <summary>The status of a sheet bound to a target.</summary>
        /// <param name="target">The collection path; <c>""</c> for the parent.</param>
        /// <returns>The status.</returns>
        private static ImportSheetStatus Status(string target)
        {
            return target.Length == 0 ? ImportSheetStatus.Parent : ImportSheetStatus.Child;
        }

        /// <summary>A sheet bound to nothing: no columns, and every column override of it unknown.</summary>
        /// <param name="binding">The sheet's binding.</param>
        /// <param name="overrides">The sheet's column overrides.</param>
        /// <returns>The sheet.</returns>
        private ImportMappedSheet Unbound(Binding binding, List<ImportColumnMapping> overrides)
        {
            foreach (ImportColumnMapping mapping in overrides)
            {
                _errors.Add(Error(binding.Sheet.Name, row: 1, mapping.Column.ToUpperInvariant(), header: null, ExcelErrorCodes.ImportUnknownColumn, []));
            }

            return new ImportMappedSheet(binding.Sheet, binding.Status, binding.Collection, Entity: null, HeaderRow: 0, Rows: 0, []);
        }

        /// <summary>Maps a bound sheet: its header row, its rows, and each header cell.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="binding">The sheet's binding.</param>
        /// <param name="sheetEntity">The entity of the sheet's rows.</param>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <param name="overrides">The sheet's column overrides.</param>
        /// <param name="cancellationToken">Stops the read between rows.</param>
        /// <returns>The sheet.</returns>
        private ImportMappedSheet MapSheet(
            WorkbookReader workbook,
            Binding binding,
            EntityMetadata sheetEntity,
            ImportManifest? manifest,
            List<ImportColumnMapping> overrides,
            CancellationToken cancellationToken)
        {
            WorkbookSheet sheet = binding.Sheet;
            ChildCollectionMetadata? collection = binding.Collection?.Collection;

            // --- The header row: the first row holding a cell that is not blank, each header kept within its bound ---
            List<(int Column, string Header, bool Overlong)> headers = [];
            int headerRow = 0;
            long rows = 0;
            using (SheetReader reader = workbook.OpenSheet(sheet))
            {
                List<SheetCell> cells = [];
                int read = 0;
                while (reader.Read(cells, out int row))
                {
                    if (cells.Any(static cell => !IsBlank(cell)))
                    {
                        headerRow = row;
                        headers.AddRange(cells.Where(static cell => !IsBlank(cell)).Select(static cell => Header(cell.Column, CellCodec.AsText(cell.Value))));
                        break;
                    }

                    if (++read % CancellationStride == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                // --- The rows below it: the dimension's, unless it ends at or before the header row (a
                // placeholder a streaming writer never patched); else counted, each row skimmed ---
                if (headerRow > 0)
                {
                    if (reader.DimensionLastRow is int last && last > headerRow)
                    {
                        rows = last - headerRow;
                    }
                    else
                    {
                        int skimmed = 0;
                        while (reader.Skim(out _, out bool filled))
                        {
                            rows += filled ? 1 : 0;
                            if (++skimmed % CancellationStride == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                    }
                }
            }

            // --- The overrides of columns the header row lacks ---
            Dictionary<int, ImportColumnMapping> byColumn = [];
            HashSet<int> headed = [.. headers.Select(static header => header.Column)];
            foreach (ImportColumnMapping mapping in overrides)
            {
                _ = ColumnLetters.TryParse(mapping.Column, out int letter);
                if (headed.Contains(letter))
                {
                    byColumn[letter] = mapping;
                }
                else
                {
                    _errors.Add(Error(sheet.Name, Math.Max(1, headerRow), ColumnLetters.Of(letter), header: null, ExcelErrorCodes.ImportUnknownColumn, []));
                }
            }

            // --- Each header cell: the override, the manifest's column table, the header index, else unmapped ---
            Dictionary<string, ImportManifestColumn> recorded = new(StringComparer.Ordinal);
            foreach (ImportManifestColumn column in manifest?.Columns ?? [])
            {
                if (string.Equals(column.Sheet, sheet.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _ = recorded.TryAdd(column.Header, column);
                }
            }

            HeaderIndex index = _cache.Headers(sheetEntity, collection, _context);
            List<ImportMappedColumn> columns = [];
            foreach ((int column, string header, bool overlong) in headers)
            {
                columns.Add(MapColumn(sheet, headerRow, sheetEntity, collection, column, header, overlong, byColumn.GetValueOrDefault(column), recorded, index));
            }

            columns = Deduplicate(columns);
            return new ImportMappedSheet(sheet, binding.Status, binding.Collection, sheetEntity, headerRow, rows, columns);
        }

        /// <summary>Resolves one header cell.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="headerRow">The header row.</param>
        /// <param name="sheetEntity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="column">The column.</param>
        /// <param name="header">The header, cut to <see cref="MaxHeaderLength" />.</param>
        /// <param name="overlong">Whether the header was cut, so that only an override maps it.</param>
        /// <param name="mapping">The column's override; null for none.</param>
        /// <param name="recorded">The manifest's columns of the sheet, by header.</param>
        /// <param name="index">The header index of the sheet's entity.</param>
        /// <returns>The column.</returns>
        private ImportMappedColumn MapColumn(
            WorkbookSheet sheet,
            int headerRow,
            EntityMetadata sheetEntity,
            ChildCollectionMetadata? collection,
            int column,
            string header,
            bool overlong,
            ImportColumnMapping? mapping,
            Dictionary<string, ImportManifestColumn> recorded,
            HeaderIndex index)
        {
            IReadOnlyList<string> languages = _context.TenantLanguages;
            ImportPathResolution resolution = default;

            // --- The override: ignored, or a path the entity carries ---
            if (mapping is not null)
            {
                if (mapping.Path is not string path)
                {
                    return new ImportMappedColumn(column, header, Path: null, ImportColumnStatus.Ignored, Language: null, []);
                }

                resolution = ImportPaths.Resolve(sheetEntity, collection, path, language: null, languages);
                if (resolution.IsUnknown)
                {
                    _errors.Add(Error(sheet.Name, headerRow, ColumnLetters.Of(column), header, ExcelErrorCodes.ImportUnknownPath, new() { ["path"] = path }));
                    return new ImportMappedColumn(column, header, Path: null, ImportColumnStatus.Unmapped, Language: null, []);
                }
            }

            // --- A header longer than any export writes matches nothing, and is offered no suggestion ---
            if (resolution.IsUnknown && overlong)
            {
                return new ImportMappedColumn(column, header, Path: null, ImportColumnStatus.Unmapped, Language: null, []);
            }

            // --- The manifest's column table by exact header, then the header index; a path the entity lacks falls through ---
            if (resolution.IsUnknown && recorded.TryGetValue(header, out ImportManifestColumn? manifestColumn))
            {
                resolution = ImportPaths.Resolve(sheetEntity, collection, manifestColumn.Path, manifestColumn.Language, languages);
            }

            if (resolution.IsUnknown && index.TryMatch(header, out HeaderTarget target))
            {
                resolution = ImportPaths.Resolve(sheetEntity, collection, target.Path, target.Language, languages);
            }

            // --- A language the tenant does not configure: ignored, with the warning ---
            if (resolution.UnconfiguredLanguage is string language)
            {
                _warnings.Add(Error(sheet.Name, headerRow, ColumnLetters.Of(column), header, ExcelErrorCodes.ImportUnconfiguredLanguageColumn, new() { ["language"] = language }));
                return new ImportMappedColumn(column, header, Path: null, ImportColumnStatus.Ignored, language, []);
            }

            // --- A resolved path: mapped when the import writes it, else server-owned ---
            return resolution.Path is ImportPath resolved
                ? new ImportMappedColumn(
                    column,
                    header,
                    resolved,
                    ImportPaths.IsImportable(sheetEntity, collection, resolved) ? ImportColumnStatus.Mapped : ImportColumnStatus.ServerOwned,
                    resolved.Language,
                    [])
                : new ImportMappedColumn(column, header, Path: null, ImportColumnStatus.Unmapped, Language: null, Suggest(header, index, sheetEntity, collection));
        }

        /// <summary>
        ///     The suggestions of an unmapped header: the paths a colliding key names, then those of
        ///     the nearest keys, resolved and distinct.
        /// </summary>
        /// <param name="header">The header.</param>
        /// <param name="index">The header index of the sheet's entity.</param>
        /// <param name="sheetEntity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <returns>At most <see cref="MaxSuggestions" /> paths.</returns>
        private List<string> Suggest(string header, HeaderIndex index, EntityMetadata sheetEntity, ChildCollectionMetadata? collection)
        {
            IEnumerable<HeaderTarget> targets = index.Collisions(header);
            if (_suggested < MaxSuggestedColumns)
            {
                _suggested++;
                targets = targets.Concat(index.Nearest(header));
            }

            List<string> suggestions = [];
            foreach (HeaderTarget target in targets)
            {
                if (ImportPaths.Resolve(sheetEntity, collection, target.Path, target.Language, _context.TenantLanguages).Path is ImportPath path
                    && !suggestions.Contains(path.Text, StringComparer.Ordinal))
                {
                    suggestions.Add(path.Text);
                    if (suggestions.Count == MaxSuggestions)
                    {
                        break;
                    }
                }
            }

            return suggestions;
        }

        /// <summary>
        ///     Marks the columns of one sheet that map to one path as duplicates: two columns writing
        ///     one property, or two server-owned columns of one path.
        /// </summary>
        /// <param name="columns">The sheet's columns.</param>
        /// <returns>The columns.</returns>
        private static List<ImportMappedColumn> Deduplicate(List<ImportMappedColumn> columns)
        {
            // A reference written in two keys writes one foreign key twice, so the written property is the identity.
            Dictionary<string, int> counts = new(StringComparer.Ordinal);
            foreach (ImportMappedColumn column in columns)
            {
                if (Identity(column) is string identity)
                {
                    counts[identity] = counts.GetValueOrDefault(identity) + 1;
                }
            }

            return [.. columns.Select(column => Identity(column) is string identity && counts[identity] > 1
                ? column with { Status = ImportColumnStatus.Duplicate }
                : column)];

            static string? Identity(ImportMappedColumn column)
            {
                return column switch
                {
                    { Status: ImportColumnStatus.Mapped, Path: ImportPath path } => "w:" + path.Property.Name,
                    { Status: ImportColumnStatus.ServerOwned, Path: ImportPath path } => "s:" + path.Text,
                    _ => null,
                };
            }
        }

        /// <summary>
        ///     Gates the identity columns of a sheet of a file that is not same-source: each mapped
        ///     <c>Id</c> and <c>Stamp</c> column is reported; a stamp is ignored, and an identity is
        ///     kept only to link the rows of a present child sheet.
        /// </summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="sheets">Every sheet of the workbook.</param>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <returns>The sheet, its identity columns gated.</returns>
        private ImportMappedSheet GateIdentity(ImportMappedSheet sheet, List<ImportMappedSheet> sheets, ImportManifest? manifest)
        {
            if (sheet.Status is not (ImportSheetStatus.Parent or ImportSheetStatus.Child))
            {
                return sheet;
            }

            // The sheet owns a present child sheet when a child sheet's collection is declared under its own.
            string own = sheet.Collection?.Path ?? string.Empty;
            bool linksChildren = sheets.Any(other => other.Status == ImportSheetStatus.Child && other.Collection?.OwnerPath == own);
            return sheet with
            {
                Columns = [.. sheet.Columns.Select(column =>
                {
                    if (column is not { Status: ImportColumnStatus.Mapped, Path.Kind: ImportPathKind.Id or ImportPathKind.Stamp })
                    {
                        return column;
                    }

                    Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
                    {
                        ["path"] = column.Path.Text,
                        ["reason"] = manifest is null ? NoManifestReason : ForeignSourceReason,
                    };
                    if (manifest?.Deployment is string deployment)
                    {
                        arguments["deployment"] = deployment;
                    }

                    if (manifest?.SourceTenantId is int sourceTenantId)
                    {
                        arguments["sourceTenantId"] = sourceTenantId;
                    }

                    _warnings.Add(Error(sheet.Sheet.Name, sheet.HeaderRow, column.Letter, column.Header, ExcelErrorCodes.ImportIdentityColumnIgnored, arguments));
                    return column.Path.Kind == ImportPathKind.Id && linksChildren ? column : column with { Status = ImportColumnStatus.Ignored };
                })],
            };
        }

        /// <summary>The plan of the mapped sheets.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="sheets">The mapped sheets.</param>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <param name="sameSource">Whether the file is same-source.</param>
        /// <returns>The plan.</returns>
        private ImportPlan Plan(WorkbookReader workbook, List<ImportMappedSheet> sheets, ImportManifest? manifest, bool sameSource)
        {
            // --- The row keys the parent sheet offers: Id on a same-source file, then each mapped natural key ---
            ImportMappedSheet? parent = sheets.FirstOrDefault(static sheet => sheet.Status == ImportSheetStatus.Parent);
            List<ImportMappedColumn> parentColumns = [.. parent?.Columns.Where(static column => column.Status == ImportColumnStatus.Mapped) ?? []];
            List<string> rowKeys = [];
            if (sameSource && parentColumns.Any(static column => column.Path!.Kind == ImportPathKind.Id))
            {
                rowKeys.Add(ImportPaths.Id);
            }

            // A multilingual key is one key named after its group's primary member (spec 0019 §6.2), so a
            // column of any of the group's slots covers it.
            foreach (NaturalKeyMetadata natural in _entity.NaturalKeys)
            {
                if (parentColumns.Any(column => column.Path!.Kind is ImportPathKind.Property or ImportPathKind.Reference && Covers(column.Path.Property.Name, natural.Property.Name))
                    && !rowKeys.Contains(natural.Property.Name, StringComparer.Ordinal))
                {
                    rowKeys.Add(natural.Property.Name);
                }
            }

            // --- The sizes against the synchronous and atomic limits ---
            long parentRows = parent?.Rows ?? 0;
            long totalRows = sheets.Where(static sheet => sheet.Status is ImportSheetStatus.Parent or ImportSheetStatus.Child).Sum(static sheet => sheet.Rows);
            int ceiling = Math.Min(_limits.MaxSaveCount, _limits.MaxIds);
            bool fitsOneSave = parentRows <= ceiling && totalRows <= _limits.MaxRowsPerSave;
            bool requiresBackground = totalRows > _options.MaxSynchronousImportRows
                || !fitsOneSave
                || workbook.CompressedBytes > _options.MaxSynchronousImportFileBytes
                || workbook.UncompressedBytes > _options.MaxSynchronousImportUncompressedBytes;
            bool atomicAllowed = totalRows <= _options.MaxAtomicImportRows && fitsOneSave;

            return new ImportPlan(
                manifest?.Entity,
                manifest?.Version ?? 0,
                manifest is not null,
                sameSource,
                manifest?.SchemaFingerprint is string fingerprint && string.Equals(fingerprint, _entity.SchemaFingerprint, StringComparison.Ordinal),
                [.. sheets.Select(static sheet => new ImportPlanSheet(
                    sheet.Sheet.Name,
                    sheet.CollectionPath,
                    sheet.Status,
                    sheet.Rows,
                    [.. sheet.Columns.Select(static column => new ImportPlanColumn(column.Letter, column.Header, column.Path?.Text, column.Status, column.Language, column.Suggestions))]))],
                rowKeys,
                rowKeys.Count > 0 ? rowKeys[0] : null,
                totalRows,
                requiresBackground,
                atomicAllowed,
                _warnings);
        }

        /// <summary>Whether a column writing a property covers a natural key: the key itself, or another slot of the key's multilingual group.</summary>
        /// <param name="property">The property the column writes.</param>
        /// <param name="key">The natural key's property.</param>
        /// <returns>True when it does.</returns>
        private bool Covers(string property, string key)
        {
            return string.Equals(property, key, StringComparison.Ordinal)
                || (ImportPaths.Slot(_entity, property) is (MultilingualGroup group, _)
                    && string.Equals(group.Primary.Name, key, StringComparison.Ordinal));
        }

        /// <summary>A header cell as the mapping keeps it: cut to <see cref="MaxHeaderLength" />, and whether it was.</summary>
        /// <param name="column">The cell's column.</param>
        /// <param name="text">The cell's text.</param>
        /// <returns>The header.</returns>
        private static (int Column, string Header, bool Overlong) Header(int column, string text)
        {
            return text.Length > MaxHeaderLength ? (column, text[..MaxHeaderLength], true) : (column, text, false);
        }

        /// <summary>Whether a stored cell is blank: no value, or text of white space alone.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>True when it is.</returns>
        private static bool IsBlank(SheetCell cell)
        {
            return cell.Value.Kind == ExcelCellKind.Blank || string.IsNullOrWhiteSpace(CellCodec.AsText(cell.Value));
        }

        /// <summary>An error or warning at a sheet's cell.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="row">The row; null for the sheet as a whole.</param>
        /// <param name="column">The column's letter; null for none.</param>
        /// <param name="header">The column's header; null for none.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The error.</returns>
        private static ImportError Error(string sheet, int? row, string? column, string? header, string code, Dictionary<string, object?> arguments)
        {
            return new ImportError(sheet, row, column, header, Property: null, code, arguments);
        }

        /// <summary>One sheet's binding.</summary>
        /// <param name="Sheet">The sheet.</param>
        /// <param name="Status">What it is bound to.</param>
        /// <param name="Target">The collection path it is bound to, <c>""</c> for the parent; null for nothing.</param>
        /// <param name="Matched">Whether an override, the manifest or the name decided the binding, so the fallback passes it over.</param>
        private sealed record Binding(WorkbookSheet Sheet, ImportSheetStatus Status, string? Target, bool Matched)
        {
            /// <summary>The collection the sheet is bound to; null for the parent and for nothing.</summary>
            public ImportCollection? Collection { get; init; }

            /// <summary>The entity of the sheet's rows; null for a sheet bound to nothing.</summary>
            public EntityMetadata? Entity { get; init; }
        }
    }
}
