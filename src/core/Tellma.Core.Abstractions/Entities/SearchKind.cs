// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>How a searchable string property matches the search text.</summary>
    public enum SearchKind
    {
        /// <summary>The value contains the text anywhere.</summary>
        Contains,

        /// <summary>The value starts with the text, which an index on the column can seek.</summary>
        Prefix,
    }
}
