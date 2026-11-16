// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The body of <c>import</c>: an uploaded workbook, what to do with its rows, and the caller's
    ///     overrides of the mapping.
    /// </summary>
    public class ImportRequest
    {
        /// <summary>The id of the caller's own staged upload of kind <c>import-file</c>; required.</summary>
        public int FileId { get; set; }

        /// <summary>What the import does with each parent row; <see cref="ImportMode.Insert" /> by default.</summary>
        public ImportMode Mode { get; set; } = ImportMode.Insert;

        /// <summary>
        ///     The column that identifies a parent row in <see cref="ImportMode.Update" /> and
        ///     <see cref="ImportMode.Upsert" />: a natural key's path, or <c>Id</c>, which only a file
        ///     exported from the same deployment and tenant accepts. Null takes the default: <c>Id</c>
        ///     on such a file, else the entity's natural key.
        /// </summary>
        public string? RowKey { get; set; }

        /// <summary>The caller's sheet mappings, at most one per sheet; empty for none.</summary>
        public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];

        /// <summary>The caller's column mappings, at most one per sheet and column; empty for none.</summary>
        public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];

        /// <summary>Whether a column the mapping cannot place is ignored; false by default, which makes it an error.</summary>
        public bool IgnoreUnmappedColumns { get; set; }

        /// <summary>
        ///     Whether the sheet's stamps are ignored, so the stamp read when each row is loaded is its
        ///     expected stamp; false by default, which makes a row edited since the export a conflict.
        /// </summary>
        public bool IgnoreSheetStamps { get; set; }
    }
}
