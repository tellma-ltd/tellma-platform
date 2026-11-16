// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>Which key of its target a reference column is expressed in.</summary>
    public enum ExcelKeyKind
    {
        /// <summary>A natural key, or another scalar property of the target.</summary>
        Natural,

        /// <summary>The target's surrogate id, for a target without a natural key.</summary>
        Surrogate,
    }
}
