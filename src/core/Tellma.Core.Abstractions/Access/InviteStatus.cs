// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The identity server's answer to the last successful invitation of a user, stored verbatim as
    ///     the member's name, <c>varchar(9)</c>. Every value leaves the user
    ///     <see cref="UserState.Invited" />.
    /// </summary>
    public enum InviteStatus
    {
        /// <summary>The identity server created the account and sent the invitation.</summary>
        Invited,

        /// <summary>The account existed without a credential, and the identity server sent the invitation again.</summary>
        Reinvited,

        /// <summary>The account already holds a credential, so the identity server sent no email.</summary>
        Active,
    }
}
