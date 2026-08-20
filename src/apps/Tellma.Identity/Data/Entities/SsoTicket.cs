// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     A server-side SSO cookie ticket, used when the optional ticket store is enabled: the
    ///     browser holds only a key, so deleting the row terminates the session immediately
    ///     (admin lock, compromise) instead of waiting for the security-stamp revalidation
    ///     interval.
    /// </summary>
    public sealed class SsoTicket
    {
        /// <summary>The opaque ticket key held by the browser cookie.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The signed-in user's id, for bulk termination.</summary>
        public string? UserId { get; set; }

        /// <summary>The serialized, Data-Protection-encrypted authentication ticket.</summary>
        public byte[] Value { get; set; } = [];

        /// <summary>When the ticket expires and becomes prunable.</summary>
        public DateTimeOffset? ExpiresUtc { get; set; }

        /// <summary>The last renewal, for sliding-expiration housekeeping.</summary>
        public DateTimeOffset LastActivityUtc { get; set; }
    }
}
