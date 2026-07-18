// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Controllers.Api
{
    /// <summary>A global-directory user, as seen by the operator surface.</summary>
    public sealed class OperatorUserResponse
    {
        /// <summary>The stable subject identifier.</summary>
        public string Sub { get; init; } = string.Empty;

        /// <summary>The user's email.</summary>
        public string? Email { get; init; }

        /// <summary>The user's display name.</summary>
        public string? DisplayName { get; init; }

        /// <summary>The user's preferred language.</summary>
        public string? Locale { get; init; }

        /// <summary>The lifecycle state (Active, Orphaned, Disabled, Purged).</summary>
        public string LifecycleState { get; init; } = string.Empty;

        /// <summary>When the account was created.</summary>
        public DateTimeOffset CreatedUtc { get; init; }
    }

    /// <summary>The issued Temporary Access Pass; the pass is shown only once.</summary>
    public sealed class TemporaryAccessPassResponse
    {
        /// <summary>The clear pass value, conveyed out-of-band to the user.</summary>
        public string Pass { get; init; } = string.Empty;

        /// <summary>When the pass expires.</summary>
        public DateTimeOffset ExpiresUtc { get; init; }
    }
}
