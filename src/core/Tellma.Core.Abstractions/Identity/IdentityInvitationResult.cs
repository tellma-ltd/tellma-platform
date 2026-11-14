// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>The identity server's answer to one <see cref="IdentityInvitation" />.</summary>
    /// <param name="Email">The invitation's address, as it was sent.</param>
    /// <param name="Subject">
    ///     The identity user's stable subject identifier; present with every
    ///     <paramref name="Status" />, and possibly with an <paramref name="Error" /> when the server
    ///     created the user before the step that failed.
    /// </param>
    /// <param name="Status">
    ///     <see cref="InviteStatus.Invited" /> (a new identity, its link queued),
    ///     <see cref="InviteStatus.Reinvited" /> (an existing credential-less identity linked again, its
    ///     link queued) or <see cref="InviteStatus.Active" /> (the identity holds a credential, so no
    ///     email was sent and the tenant tells the user itself); null when the user was refused.
    /// </param>
    /// <param name="Error">The server's per-user refusal, verbatim; null on success.</param>
    public sealed record IdentityInvitationResult(
        string Email,
        string? Subject,
        InviteStatus? Status,
        string? Error);
}
