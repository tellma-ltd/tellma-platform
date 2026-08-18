// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Identity.Controllers.Api
{
    /// <summary>The bulk-invitation request body.</summary>
    public sealed class InviteUsersRequest
    {
        /// <summary>The users to invite (1–1000).</summary>
        [Required]
        [MinLength(1)]
        [MaxLength(1000)]
        public IList<InviteUserItem> Users { get; init; } = [];
    }

    /// <summary>One user to invite.</summary>
    public sealed class InviteUserItem
    {
        /// <summary>The user's email.</summary>
        [Required]
        [EmailAddress]
        public string Email { get; init; } = string.Empty;

        /// <summary>The user's display name.</summary>
        public string? DisplayName { get; init; }

        /// <summary>The user's preferred language (BCP 47).</summary>
        public string? Locale { get; init; }

        /// <summary>
        ///     How to address the user grammatically (<c>female</c>, <c>male</c>). Optional:
        ///     omitted means unstated, and every message has a neutral form.
        /// </summary>
        public string? Gender { get; init; }

        /// <summary>Where the accepted invitation returns the user (validated against the client).</summary>
        public string? ReturnUrl { get; init; }
    }

    /// <summary>The bulk delivery-status request body.</summary>
    public sealed class InvitationDeliveryStatusRequest
    {
        /// <summary>The users to report on (1–1000), by the <c>sub</c> the invite call returned.</summary>
        [Required]
        [MinLength(1)]
        [MaxLength(1000)]
        public IList<string> Subs { get; init; } = [];
    }

    /// <summary>The bulk delivery-status response body.</summary>
    public sealed class InvitationDeliveryStatusResponse
    {
        /// <summary>One result per requested subject, in request order.</summary>
        public IList<InvitationDeliveryStatusResult> Results { get; init; } = [];
    }

    /// <summary>One user's invitation delivery status.</summary>
    public sealed class InvitationDeliveryStatusResult
    {
        /// <summary>The subject asked about.</summary>
        public string Sub { get; init; } = string.Empty;

        /// <summary>
        ///     How far the invitation got: <c>NotFound</c>, <c>Pending</c>, <c>Sent</c>,
        ///     <c>Delivered</c>, <c>Bounced</c>, <c>Complained</c>, <c>Rejected</c>,
        ///     <c>Abandoned</c>, or <c>Accepted</c>.
        ///     <para>
        ///         <c>NotFound</c> means this caller raised no invitation for that subject. It is
        ///         deliberately the same answer for a user that does not exist, so that the endpoint
        ///         cannot be used to probe the global directory.
        ///     </para>
        /// </summary>
        public string State { get; init; } = string.Empty;

        /// <summary>
        ///     Whether a provider will report further on this message. When false, <c>Sent</c> is
        ///     the end of the story rather than a step on the way to <c>Delivered</c> — an
        ///     on-premise SMTP relay reports nothing back, and a caller that renders silence as
        ///     "not delivered yet" would be showing a status that can never change.
        /// </summary>
        public bool ExpectsDeliveryEvents { get; init; }

        /// <summary>When the message reached a transport; null until it has.</summary>
        public DateTimeOffset? SentUtc { get; init; }

        /// <summary>When a provider last reported on it; null when none has.</summary>
        public DateTimeOffset? UpdatedUtc { get; init; }

        /// <summary>The provider's failure detail, for a failed state only.</summary>
        public string? Reason { get; init; }
    }

    /// <summary>The bulk-invitation response body. Never contains invitation links.</summary>
    public sealed class InviteUsersResponse
    {
        /// <summary>
        ///     The per-user results, in request order — one per requested user, except when the
        ///     caller abandons the request, which stops the batch after the users already
        ///     processed (whose invitations are still delivered).
        /// </summary>
        public IList<InviteUserResult> Results { get; init; } = [];
    }

    /// <summary>One user's invitation result: a status with the subject, or an error — never both.</summary>
    public sealed class InviteUserResult
    {
        /// <summary>The invited email.</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>The user's stable subject identifier; null when the user was refused.</summary>
        public string? Sub { get; init; }

        /// <summary>
        ///     The per-user outcome: <c>Invited</c>, <c>Reinvited</c>, or <c>Active</c>; null when
        ///     the user was refused.
        /// </summary>
        public string? Status { get; init; }

        /// <summary>Why the user was refused (the batch continues past it); null on success.</summary>
        public string? Error { get; init; }
    }
}
