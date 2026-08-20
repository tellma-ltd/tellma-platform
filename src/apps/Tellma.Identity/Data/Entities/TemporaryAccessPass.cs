// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     An operator-issued Temporary Access Pass for admin-assisted recovery: short-lived
    ///     (at most one hour), single-use, shown to the operator once and conveyed out-of-band.
    ///     Using it grants a limited session whose only exit is enrolling a new passkey; it can
    ///     never be used for normal sign-in.
    /// </summary>
    public sealed class TemporaryAccessPass
    {
        /// <summary>The row id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The user the pass recovers.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Base64 SHA-256 hash of the pass; the clear value is never stored.</summary>
        public string SecretHash { get; set; } = string.Empty;

        /// <summary>The operator client that issued the pass (audited).</summary>
        public string? IssuedByClientId { get; set; }

        /// <summary>When the pass was issued.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>When the pass expires (at most one hour after issuance).</summary>
        public DateTimeOffset ExpiresUtc { get; set; }

        /// <summary>When the pass was consumed; a consumed pass never verifies again.</summary>
        public DateTimeOffset? ConsumedUtc { get; set; }
    }
}
