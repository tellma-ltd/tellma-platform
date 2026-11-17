// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>The shape of an exported workbook.</summary>
    public enum ExportKind
    {
        /// <summary>
        ///     The grid as a sheet: the columns the user sees, in the user's language, each value as it
        ///     reads. For reading, not for importing back.
        /// </summary>
        Display,

        /// <summary>
        ///     The entity as rectangular tables that import back unchanged: every editable column, a
        ///     sheet per child collection at every depth, references as natural keys, and a hidden
        ///     manifest.
        /// </summary>
        ForImport,

        /// <summary>A report's table, written in the display shape; never imported.</summary>
        Report,
    }
}
