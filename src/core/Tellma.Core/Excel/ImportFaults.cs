// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The import faults that refuse a workbook as a whole: a package above a size limit, a
    ///     workbook above the synchronous import's limits, a shared-string table with too many
    ///     entries, and a package or part that is not a readable workbook. Each is an <see cref="ImportException" /> carrying one error, at the sheet when one
    ///     is to blame and at no row.
    /// </summary>
    internal static class ImportFaults
    {
        /// <summary>The reason of a zip archive that cannot be read, or whose directory is out of bounds.</summary>
        public const string ZipReason = "zip";

        /// <summary>The reason of a part that is not well-formed XML, holds a document type, or a token above the bound.</summary>
        public const string XmlReason = "xml";

        /// <summary>The reason of a package without a readable <c>[Content_Types].xml</c>.</summary>
        public const string ContentTypesReason = "content-types";

        /// <summary>The reason of a package or workbook whose relationships are missing or name a part the package lacks.</summary>
        public const string RelationshipsReason = "relationships";

        /// <summary>The reason of a package that holds no workbook part, or more than one.</summary>
        public const string WorkbookReason = "workbook";

        /// <summary>The reason of a workbook that links another workbook.</summary>
        public const string ExternalLinkReason = "external-link";

        /// <summary>The reason of a package or workbook relationship that targets something outside the package.</summary>
        public const string ExternalRelationshipReason = "external-relationship";

        /// <summary>The reason of a workbook whose sheets are missing, too many, unnamed or named twice.</summary>
        public const string SheetsReason = "sheets";

        /// <summary>The reason of a sheet whose rows or cells are out of order, out of range or misaddressed.</summary>
        public const string CellsReason = "cells";

        /// <summary>The reason of a manifest that names another entity than the one imported into.</summary>
        public const string EntityReason = "entity";

        /// <summary>A package above a size limit.</summary>
        /// <param name="bytes">
        ///     The size found; the bytes read when a part ran past its declared length; or the bytes
        ///     a part's distinct names were charged when they passed their bound.
        /// </param>
        /// <param name="max">The limit.</param>
        /// <returns>The exception.</returns>
        public static ImportException TooLarge(long bytes, long max)
        {
            return One(ExcelErrorCodes.ImportPackageTooLarge, sheet: null, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["bytes"] = bytes,
                ["max"] = max,
            });
        }

        /// <summary>A shared-string table with more entries than allowed.</summary>
        /// <param name="entries">The entries the table holds.</param>
        /// <param name="max">The limit.</param>
        /// <returns>The exception.</returns>
        public static ImportException TooManySharedStrings(long entries, int max)
        {
            return One(ExcelErrorCodes.ImportTooManySharedStrings, sheet: null, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["entries"] = entries,
                ["max"] = max,
            });
        }

        /// <summary>A workbook above a limit of the synchronous import, which the background import runs instead.</summary>
        /// <param name="rows">The data rows the workbook holds against the row limit: every sheet's, or the parent sheet's against the parent ceiling.</param>
        /// <param name="maxRows">The row limit: the one crossed, else the synchronous import's.</param>
        /// <param name="bytes">The workbook's bytes against the byte limit: its uncompressed parts' when that limit was crossed, else the file's.</param>
        /// <param name="maxBytes">The byte limit: the one crossed, else the file's.</param>
        /// <returns>The exception.</returns>
        public static ImportException TooLargeForSynchronous(long rows, long maxRows, long bytes, long maxBytes)
        {
            return One(ExcelErrorCodes.ImportTooLargeForSynchronous, sheet: null, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rows"] = rows,
                ["maxRows"] = maxRows,
                ["bytes"] = bytes,
                ["maxBytes"] = maxBytes,
            });
        }

        /// <summary>An atomic background import above a ceiling of its one transaction.</summary>
        /// <param name="rows">The rows the file holds against the ceiling crossed: its rows at every depth, or its parent rows against the parent ceiling.</param>
        /// <param name="maxRows">The ceiling crossed.</param>
        /// <returns>The exception.</returns>
        public static ImportException TooLargeForAtomic(long rows, long maxRows)
        {
            return One(ExcelErrorCodes.ImportTooLargeForAtomic, sheet: null, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rows"] = rows,
                ["maxRows"] = maxRows,
            });
        }

        /// <summary>A resumed background import whose file or mapping no longer orders its rows as its checkpoint did.</summary>
        /// <param name="expected">The checkpoint's order hash.</param>
        /// <param name="actual">The order hash the resumed parse computed.</param>
        /// <returns>The exception.</returns>
        public static ImportException ResumeMismatch(string expected, string actual)
        {
            return One(ExcelErrorCodes.ImportResumeMismatch, sheet: null, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["expected"] = expected,
                ["actual"] = actual,
            });
        }

        /// <summary>A package or part that is not a readable workbook.</summary>
        /// <param name="reason">Why: one of this class's reason constants.</param>
        /// <param name="sheet">The sheet to blame; null for the package.</param>
        /// <returns>The exception.</returns>
        public static ImportException Malformed(string reason, string? sheet = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            return One(ExcelErrorCodes.ImportMalformedWorkbook, sheet, new Dictionary<string, object?>(StringComparer.Ordinal) { ["reason"] = reason });
        }

        /// <summary>An exception of one error at a sheet, or at the package, and at no row.</summary>
        /// <param name="code">The error code.</param>
        /// <param name="sheet">The sheet; null for the package.</param>
        /// <param name="arguments">The error's arguments.</param>
        /// <returns>The exception.</returns>
        private static ImportException One(string code, string? sheet, IReadOnlyDictionary<string, object?> arguments)
        {
            return new ImportException([new ImportError(sheet, Row: null, Column: null, Header: null, Property: null, code, arguments)], totalErrors: 1, committedRowRanges: []);
        }
    }
}
