// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>What an import does with each parent row of the workbook.</summary>
    public enum ImportMode
    {
        /// <summary>Every row is a new row; an <c>Id</c> only links rows between sheets.</summary>
        Insert,

        /// <summary>Every row names an existing row by its row key; a row that names none is an error.</summary>
        Update,

        /// <summary>A row that names an existing row by its row key updates it; any other row is inserted.</summary>
        Upsert,
    }
}
