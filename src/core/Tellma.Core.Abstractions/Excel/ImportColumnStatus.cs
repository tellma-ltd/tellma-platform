// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>What an import plan made of one column of a sheet.</summary>
    public enum ImportColumnStatus
    {
        /// <summary>The column maps to a path, and its cells are imported.</summary>
        Mapped,

        /// <summary>Nothing places the column, which is an error unless unmapped columns are ignored.</summary>
        Unmapped,

        /// <summary>The column is not read: the caller ignored it, or its value has no home.</summary>
        Ignored,

        /// <summary>The column is one of two mapped to the same path, which is an error.</summary>
        Duplicate,

        /// <summary>The column maps to a path the server writes, which an import may not carry.</summary>
        ServerOwned,
    }
}
