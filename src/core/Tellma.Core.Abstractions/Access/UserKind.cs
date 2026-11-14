// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     What a user row stands for. Stored as the member's name, <c>varchar(10)</c>, and fixed when
    ///     the row is created.
    /// </summary>
    public enum UserKind
    {
        /// <summary>A person, who signs in through the identity server with their email address.</summary>
        Individual,

        /// <summary>
        ///     The tenant's own system user, row 1 only: the author of seeded rows and the identity of
        ///     built-in work. It has no subject and never signs in.
        /// </summary>
        System,

        /// <summary>A service account, which authenticates with client credentials and has no email address.</summary>
        Service,
    }
}
