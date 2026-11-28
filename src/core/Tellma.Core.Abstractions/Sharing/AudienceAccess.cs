// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>What an audience user may do with a shareable row, lowest first.</summary>
    public enum AudienceAccess
    {
        /// <summary>See the row.</summary>
        View,

        /// <summary>See the row and save changes to it: an edit grant, or a stored permission to save or to do anything.</summary>
        Edit,

        /// <summary>Own the row.</summary>
        Owner,
    }
}
