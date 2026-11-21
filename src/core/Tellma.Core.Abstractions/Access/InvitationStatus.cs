// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Identity;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A user's invitation as the users' <c>invitation-status</c> operation reports it: the stored
    ///     state and outcome, and the delivery of the invitation email where one was raised.
    /// </summary>
    /// <remarks>
    ///     <see cref="Delivery" /> is null for a user who was never invited, or who has signed in. A
    ///     user whose invitation answered <see cref="InviteStatus.Active" /> needed no email: the
    ///     delivery reads <see cref="IdentityDeliveryState.NotFound" /> without asking the identity
    ///     server. Otherwise it is the identity server's answer, where
    ///     <see cref="IdentityDeliveryState.NotFound" /> means the invitation was raised by another
    ///     client registration.
    /// </remarks>
    /// <param name="Id">The user's id.</param>
    /// <param name="State">How far the user has come.</param>
    /// <param name="InviteStatus">The identity server's answer to the user's last successful invitation; null when none succeeded.</param>
    /// <param name="LastInviteError">Why the user's last invitation failed; null when it succeeded or none was made.</param>
    /// <param name="Delivery">The invitation email's delivery; null when no email is expected.</param>
    public sealed record InvitationStatus(
        int Id,
        UserState State,
        InviteStatus? InviteStatus,
        string? LastInviteError,
        IdentityDeliveryStatus? Delivery);
}
