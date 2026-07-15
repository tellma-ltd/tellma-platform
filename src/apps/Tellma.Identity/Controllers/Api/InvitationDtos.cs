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

        /// <summary>Where the accepted invitation returns the user (validated against the client).</summary>
        public string? ReturnUrl { get; init; }
    }

    /// <summary>The bulk-invitation response body. Never contains invitation links.</summary>
    public sealed class InviteUsersResponse
    {
        /// <summary>The per-user results, in request order.</summary>
        public IList<InviteUserResult> Results { get; init; } = [];
    }

    /// <summary>One user's invitation result.</summary>
    public sealed class InviteUserResult
    {
        /// <summary>The invited email.</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>The user's stable subject identifier.</summary>
        public string Sub { get; init; } = string.Empty;

        /// <summary>The per-user outcome: <c>Invited</c>, <c>Reinvited</c>, or <c>Active</c>.</summary>
        public string Status { get; init; } = string.Empty;
    }
}
