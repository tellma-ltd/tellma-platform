// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     How far a user has come, as the tenant's own evidence shows it. The database computes it from
    ///     the user's invitation and join timestamps and stores it as the member's name,
    ///     <c>varchar(8)</c>; the application never writes it.
    /// </summary>
    public enum UserState
    {
        /// <summary>Created, and neither invited nor joined.</summary>
        New,

        /// <summary>Invited through the identity server, and not yet signed in to the tenant.</summary>
        Invited,

        /// <summary>
        ///     An individual who signed in to the tenant at least once, or a service account that holds
        ///     credentials.
        /// </summary>
        Joined,
    }
}
