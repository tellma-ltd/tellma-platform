// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The body of <c>inspect-import</c>: an uploaded workbook and the caller's overrides, which
    ///     the server maps into an import plan without importing anything.
    /// </summary>
    public sealed class InspectImportRequest
    {
        /// <summary>The id of the caller's own staged upload of kind <c>import-file</c>; required.</summary>
        public int FileId { get; set; }

        /// <summary>The caller's sheet mappings, at most one per sheet; empty for none.</summary>
        public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];

        /// <summary>The caller's column mappings, at most one per sheet and column; empty for none.</summary>
        public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];
    }
}
