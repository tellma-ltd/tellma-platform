// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>What brings an audience user to a shareable row.</summary>
    public enum AudienceReasonKind
    {
        /// <summary>The user owns the row.</summary>
        Owner,

        /// <summary>A grant names the user.</summary>
        UserShare,

        /// <summary>A grant names a role the user currently belongs to, or a public role.</summary>
        RoleShare,

        /// <summary>The Everyone grant.</summary>
        Everyone,

        /// <summary>A stored permission of a role the user currently belongs to, or of a public role, grants an action on the row's resource.</summary>
        Permission,

        /// <summary>The user sees a row whose audience also sees this one.</summary>
        Through,
    }
}
