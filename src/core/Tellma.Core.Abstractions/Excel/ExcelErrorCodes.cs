// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The Excel export's and import's codes: the dotted PascalCase resource keys hosts localize
    ///     through the request culture. <see cref="ImportUnconfiguredLanguageColumn" /> and
    ///     <see cref="ImportIdentityColumnIgnored" /> are warnings; every other code is an error.
    /// </summary>
    /// <remarks>
    ///     Codes are stable: a platform minor may add one, never rename one. An import error places
    ///     itself at its workbook coordinates (sheet, row, column), each code listing the ones it
    ///     carries; codes the save pipeline raises keep their own names when translated to a cell.
    /// </remarks>
    public static class ExcelErrorCodes
    {
        // --- Export ---

        /// <summary>
        ///     A sheet's rows exceed its cap; raised as a limit-exceeded failure whose limit is this
        ///     code. Arguments: <c>limit</c>, <c>actual</c> (the rows), <c>maximum</c> (the cap).
        /// </summary>
        public const string ExportRowLimitExceeded = "Excel.Export.RowLimitExceeded";

        /// <summary>
        ///     A text value an editable export must carry whole is longer than an Excel cell holds;
        ///     raised as a limit-exceeded failure whose limit is this code. Arguments: <c>limit</c>,
        ///     <c>actual</c> (the characters), <c>maximum</c> (32,767).
        /// </summary>
        public const string ExportCellTooLong = "Excel.Export.CellTooLong";

        // --- Import: file and package limits ---

        /// <summary>A synchronous import above its limits. Arguments: <c>rows</c>, <c>maxRows</c>, <c>bytes</c>, <c>maxBytes</c>.</summary>
        public const string ImportTooLargeForSynchronous = "Excel.Import.TooLargeForSynchronous";

        /// <summary>An import that must commit in one transaction is above the atomic limits. Arguments: <c>rows</c>, <c>maxRows</c>.</summary>
        public const string ImportTooLargeForAtomic = "Excel.Import.TooLargeForAtomic";

        /// <summary>The workbook's package is above a size limit. Arguments: <c>bytes</c>, <c>max</c>.</summary>
        public const string ImportPackageTooLarge = "Excel.Import.PackageTooLarge";

        /// <summary>The shared-string part holds more entries than allowed. Arguments: <c>entries</c>, <c>max</c>.</summary>
        public const string ImportTooManySharedStrings = "Excel.Import.TooManySharedStrings";

        /// <summary>
        ///     The package's structure or XML is broken, or its manifest names another entity. At the
        ///     sheet when one is to blame. Argument: <c>reason</c>.
        /// </summary>
        public const string ImportMalformedWorkbook = "Excel.Import.MalformedWorkbook";

        // --- Import: sheet and column mapping ---

        /// <summary>A sheet or column mapping names a sheet the workbook lacks. At the sheet.</summary>
        public const string ImportUnknownSheet = "Excel.Import.UnknownSheet";

        /// <summary>Two sheets are bound to one target; raised at both. At the sheet. Argument: <c>collection</c>.</summary>
        public const string ImportDuplicateSheet = "Excel.Import.DuplicateSheet";

        /// <summary>
        ///     A child sheet's owner sheet is absent, ignored or maps no <c>Id</c> column. At the child
        ///     sheet. Arguments: <c>collection</c>, <c>owner</c>.
        /// </summary>
        public const string ImportOwnerSheetAbsent = "Excel.Import.OwnerSheetAbsent";

        /// <summary>A column maps to nothing and unmapped columns are not ignored. At the header cell. Arguments: <c>header</c>, <c>suggestions</c>.</summary>
        public const string ImportUnmappedColumn = "Excel.Import.UnmappedColumn";

        /// <summary>A column mapping names a column with no header cell, or on a sheet the plan ignores. At the header cell.</summary>
        public const string ImportUnknownColumn = "Excel.Import.UnknownColumn";

        /// <summary>A column mapping's path is not a column path of the sheet's entity. At the header cell. Argument: <c>path</c>.</summary>
        public const string ImportUnknownPath = "Excel.Import.UnknownPath";

        /// <summary>Two columns map to one path. At the header cell. Argument: <c>path</c>.</summary>
        public const string ImportDuplicateColumn = "Excel.Import.DuplicateColumn";

        /// <summary>A column maps to a path the server writes, derives or excludes. At the header cell. Argument: <c>path</c>.</summary>
        public const string ImportServerOwnedColumn = "Excel.Import.ServerOwnedColumn";

        /// <summary>
        ///     Warning: a multilingual column's language is not configured in the target tenant, so the
        ///     column is ignored. At the header cell. Argument: <c>language</c>.
        /// </summary>
        public const string ImportUnconfiguredLanguageColumn = "Excel.Import.UnconfiguredLanguageColumn";

        /// <summary>
        ///     Warning: an <c>Id</c> or <c>Stamp</c> column is mapped but does not key rows or carry
        ///     their expected stamps. At the header cell. Arguments: <c>path</c>, <c>reason</c>
        ///     (<c>ForeignSource</c>, <c>NoManifest</c> or <c>InsertMode</c>), and <c>deployment</c> and
        ///     <c>sourceTenantId</c> when the manifest names them.
        /// </summary>
        public const string ImportIdentityColumnIgnored = "Excel.Import.IdentityColumnIgnored";

        // --- Import: rows and keys ---

        /// <summary>A value that may be set only once differs from the stored one on update. At the cell. Argument: <c>path</c>.</summary>
        public const string ImportWriteOnceChanged = "Excel.Import.WriteOnceChanged";

        /// <summary>A row key resolves to no readable row. At the key cell. Arguments: <c>key</c>, <c>value</c>.</summary>
        public const string ImportRowNotFound = "Excel.Import.RowNotFound";

        /// <summary>A row key or unique value repeats within the sheet. At the key cell. Argument: <c>value</c>.</summary>
        public const string ImportDuplicateRowKey = "Excel.Import.DuplicateRowKey";

        /// <summary>Rows keyed by id from a file another deployment or tenant exported. Arguments: <c>deployment</c>, <c>sourceTenantId</c>.</summary>
        public const string ImportIdKeyRequiresSameTenant = "Excel.Import.IdKeyRequiresSameTenant";

        /// <summary>A reference column keyed by id in a file another deployment or tenant exported. At the column. Argument: <c>entity</c>.</summary>
        public const string ImportSurrogateReferenceFromOtherTenant = "Excel.Import.SurrogateReferenceFromOtherTenant";

        /// <summary>A reference matches no readable row. At the cell. Arguments: <c>entity</c>, <c>key</c>, <c>value</c>, and <c>collation</c> when it applies.</summary>
        public const string ImportReferenceNotFound = "Excel.Import.ReferenceNotFound";

        /// <summary>A reference matches more than one row. At the cell. Arguments: <c>entity</c>, <c>key</c>, <c>value</c>, <c>count</c>.</summary>
        public const string ImportReferenceAmbiguous = "Excel.Import.ReferenceAmbiguous";

        /// <summary>A key value is longer than a lookup accepts. At the cell. Argument: <c>max</c>.</summary>
        public const string ImportKeyTooLong = "Excel.Import.KeyTooLong";

        // --- Import: child sheets and trees ---

        /// <summary>A child row's parent reference matches no row of its owner sheet. At the parent reference cell. Argument: <c>value</c>.</summary>
        public const string ImportOrphanChildRow = "Excel.Import.OrphanChildRow";

        /// <summary>
        ///     A collection to be replaced whose sheet lacks an editable column, or whose descendant
        ///     sheets are not all present. At the child sheet. Arguments: <c>collection</c>,
        ///     <c>missingColumns</c>, <c>missingCollections</c>.
        /// </summary>
        public const string ImportIncompleteChildSheet = "Excel.Import.IncompleteChildSheet";

        /// <summary>New rows reference each other in a cycle no order can satisfy. At the self-reference cell. Arguments: <c>rows</c>, <c>path</c>.</summary>
        public const string ImportParentCycle = "Excel.Import.ParentCycle";

        /// <summary>
        ///     More rows under one owner row in a collection than the collection allows. At the first
        ///     row beyond the cap. Arguments: <c>collection</c>, <c>max</c>, <c>count</c>.
        /// </summary>
        public const string ImportTooManyChildren = "Excel.Import.TooManyChildren";

        /// <summary>More rows under one parent row, at every depth together, than one save carries. At the parent row. Arguments: <c>rows</c>, <c>max</c>.</summary>
        public const string ImportTooManyDescendants = "Excel.Import.TooManyDescendants";

        // --- Import: cells, saving and jobs ---

        /// <summary>
        ///     A cell does not decode to its column's type. At the cell. Arguments: <c>expected</c> (the
        ///     expected kind of value), <c>value</c> (the cell's text, cut to 100 characters).
        /// </summary>
        public const string ImportInvalidCell = "Excel.Import.InvalidCell";

        /// <summary>The row changed since the sheet's stamp or its hydration. At the row. Arguments: <c>modifiedAt</c>, <c>modifiedBy</c>.</summary>
        public const string ImportConcurrencyConflict = "Excel.Import.ConcurrencyConflict";

        /// <summary>A resumed background import read a file whose row order differs from its checkpoint's. Arguments: <c>expected</c>, <c>actual</c>.</summary>
        public const string ImportResumeMismatch = "Excel.Import.ResumeMismatch";

        /// <summary>The error code of a failed background import's job.</summary>
        public const string ImportFailed = "Excel.Import.Failed";

        /// <summary>Deleting an import record whose job has not ended.</summary>
        public const string ImportsJobActive = "Imports.JobActive";
    }
}
