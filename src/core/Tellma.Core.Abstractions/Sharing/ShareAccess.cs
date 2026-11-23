// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>What a grant of a shareable row lets its grantee do. Stored by name.</summary>
    public enum ShareAccess
    {
        /// <summary>See the row, and run it where the entity runs.</summary>
        View,

        /// <summary>See the row and save changes to it; never share it further.</summary>
        Edit,
    }
}
