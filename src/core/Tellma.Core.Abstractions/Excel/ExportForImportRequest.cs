// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The body of an editable-shape <c>export-for-import</c>: the rows, chosen as
    ///     <see cref="ExportSourceRequest" /> chooses them, and which columns, reference keys and child
    ///     collections the workbook carries.
    /// </summary>
    /// <remarks>
    ///     An empty <see cref="ExportSourceRequest.Ids" /> list is the entity's template: headers,
    ///     validation lists and the manifest, with no rows.
    /// </remarks>
    public sealed class ExportForImportRequest : ExportSourceRequest
    {
        /// <summary>
        ///     The parent's editable column paths to restrict the sheet to; null for all. <c>Id</c>,
        ///     <c>Stamp</c> and the default natural key's column are always kept; a path that is not
        ///     editable is a bad request.
        /// </summary>
        public IReadOnlyList<string>? Columns { get; set; }

        /// <summary>
        ///     Per foreign-key property, the target property to express it in (<c>CenterId</c> →
        ///     <c>Code</c>), overriding the target's default natural key; any scalar property of the
        ///     target is allowed.
        /// </summary>
        public IReadOnlyDictionary<string, string> ReferenceKeys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        ///     The child collections that get a sheet: null for every collection at every depth, empty
        ///     for none, else that subset. A collection left out has no sheet, so importing the file
        ///     back leaves it untouched.
        /// </summary>
        public IReadOnlyList<ExportCollection>? Children { get; set; }
    }
}
