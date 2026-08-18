// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Invitations
{
    /// <summary>How far an invitation got, as a distribution needs to read it.</summary>
    public enum InvitationDeliveryState
    {
        /// <summary>
        ///     No invitation this caller raised. Deliberately the same answer for a user that does
        ///     not exist and one another distribution invited: telling those apart would let any
        ///     caller with the scope probe the global directory a subject at a time.
        /// </summary>
        NotFound = 0,

        /// <summary>Raised, but the mail has not reached a transport yet. The sweep will finish it.</summary>
        Pending = 1,

        /// <summary>Accepted by a transport. Terminal unless the message expects delivery events.</summary>
        Sent = 2,

        /// <summary>Accepted by the recipient's mail server.</summary>
        Delivered = 3,

        /// <summary>Rejected by the recipient's mail server, or withheld by the provider.</summary>
        Bounced = 4,

        /// <summary>Delivered, and then reported as spam by the recipient.</summary>
        Complained = 5,

        /// <summary>Refused permanently by the transport, typically an undeliverable address.</summary>
        Rejected = 6,

        /// <summary>Retried to the limit without ever being accepted.</summary>
        Abandoned = 7,

        /// <summary>The recipient opened the link. Nothing more is owed here.</summary>
        Accepted = 8,
    }

    /// <summary>One user's invitation delivery status.</summary>
    /// <param name="Subject">The user's stable subject identifier, as asked for.</param>
    /// <param name="State">How far the invitation got.</param>
    /// <param name="ExpectsDeliveryEvents">Whether a provider will report further on this message.
    ///     False makes <see cref="InvitationDeliveryState.Sent" /> the end of the story — an
    ///     on-premise relay reports nothing back, and silence there means no feedback exists, never
    ///     that delivery failed.</param>
    /// <param name="SentUtc">When the message reached a transport; null until it has.</param>
    /// <param name="UpdatedUtc">When a provider last reported; null when none has.</param>
    /// <param name="Reason">The provider's failure detail, for a failed state only.</param>
    public sealed record InvitationDeliveryStatus(
        string Subject,
        InvitationDeliveryState State,
        bool ExpectsDeliveryEvents,
        DateTimeOffset? SentUtc,
        DateTimeOffset? UpdatedUtc,
        string? Reason);

    /// <summary>
    ///     Answers what became of the invitations a distribution raised, so its admin can tell a
    ///     bounced address from one that simply has not been opened yet.
    /// </summary>
    /// <remarks>
    ///     Every read is scoped to the calling client. That is the whole security model here: the
    ///     server maps no user to a tenant, so the only thing that makes one distribution's
    ///     invitations distinguishable from another's is which client raised them.
    /// </remarks>
    /// <param name="store">The identity store.</param>
    public sealed class InvitationDeliveryStatusService(TellmaIdentityDbContext store)
    {
        /// <summary>The most invitations one call may ask about, matching the invite API's own cap.</summary>
        public const int MaxSubjects = 1000;

        /// <summary>Reads the delivery status of the invitations a client raised.</summary>
        /// <param name="subjects">The users to report on, in the order asked.</param>
        /// <param name="clientId">The calling client; never null — the caller checks first.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>One result per requested subject, in request order.</returns>
        public async Task<IReadOnlyList<InvitationDeliveryStatus>> ReadAsync(
            IReadOnlyList<string> subjects, string clientId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(subjects);
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

            string[] distinct = [.. subjects.Distinct(StringComparer.Ordinal)];

            // The latest invitation per user, because a re-invite supersedes its predecessor and
            // only the current one describes what the recipient is holding.
            Dictionary<string, SingleUseCode> latest = await store.Set<SingleUseCode>()
                .Where(c => c.Purpose == SingleUseCodePurpose.Invitation)
                .Where(c => c.CreatedByClientId == clientId)
                .Where(c => distinct.Contains(c.UserId))
                .GroupBy(static c => c.UserId)
                .Select(static group => group.OrderByDescending(static c => c.CreatedUtc).First())
                .ToDictionaryAsync(static c => c.UserId, StringComparer.Ordinal, cancellationToken);

            // One result per requested subject, in request order — including the duplicates a
            // caller may have sent, so the two lists line up positionally the way the invite API's
            // do.
            return [.. subjects.Select(subject =>
                latest.TryGetValue(subject, out SingleUseCode? row)
                    ? Describe(subject, row)
                    : new InvitationDeliveryStatus(
                        subject, InvitationDeliveryState.NotFound, false, null, null, null))];
        }

        /// <summary>Turns a row into what the caller is told about it.</summary>
        private static InvitationDeliveryStatus Describe(string subject, SingleUseCode row)
        {
            InvitationDeliveryState state = StateOf(row);

            // The reason is a provider's failure text and is only meaningful for a failure. On a
            // success it would be noise at best, and at worst a detail about the recipient's mail
            // system that nothing asked for.
            string? reason = state is InvitationDeliveryState.Bounced
                or InvitationDeliveryState.Rejected
                or InvitationDeliveryState.Abandoned
                ? row.DeliveryReason
                : null;

            return new InvitationDeliveryStatus(
                subject, state, row.ExpectsDeliveryEvents, row.SentUtc, row.DeliveryUpdatedUtc, reason);
        }

        /// <summary>Reduces a row's dispatch and delivery columns to one state.</summary>
        private static InvitationDeliveryState StateOf(SingleUseCode row)
        {
            // Redemption outranks everything: once the link has been opened, what the provider
            // said about the mail carrying it is history.
            return row.ConsumedUtc is not null
                ? InvitationDeliveryState.Accepted
                : row.DispatchState switch
                {
                    EmailDispatchState.Pending => InvitationDeliveryState.Pending,
                    EmailDispatchState.Rejected => InvitationDeliveryState.Rejected,
                    EmailDispatchState.Abandoned => InvitationDeliveryState.Abandoned,

                    // Sent or sandboxed: whatever the provider has since said, or simply Sent when
                    // it has said nothing — the permanent answer on a transport that never will.
                    EmailDispatchState.Sent or EmailDispatchState.Sandboxed => Reported(row.DeliveryStatus),
                    _ => InvitationDeliveryState.Sent,
                };
        }

        /// <summary>Reduces a provider's last report to a state the caller can act on.</summary>
        private static InvitationDeliveryState Reported(EmailDeliveryStatus? status)
        {
            return status switch
            {
                EmailDeliveryStatus.Delivered => InvitationDeliveryState.Delivered,
                EmailDeliveryStatus.SpamReported => InvitationDeliveryState.Complained,
                EmailDeliveryStatus.Bounced
                    or EmailDeliveryStatus.Dropped
                    or EmailDeliveryStatus.Failed => InvitationDeliveryState.Bounced,

                // Deferred and Other both say the message is still in the provider's hands, which
                // is what Sent already means; null says it has reported nothing at all.
                EmailDeliveryStatus.Deferred or EmailDeliveryStatus.Other or null =>
                    InvitationDeliveryState.Sent,
                _ => InvitationDeliveryState.Sent,
            };
        }
    }
}
