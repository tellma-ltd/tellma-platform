// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     An immutable audit record of a security-relevant action. Rows are append-only: no
    ///     update or delete path exists in the engine, and the subject is deliberately not a
    ///     foreign key so audit history outlives everything it references.
    /// </summary>
    public sealed class AuditEvent
    {
        /// <summary>The row id.</summary>
        public long Id { get; set; }

        /// <summary>When the action happened.</summary>
        public DateTimeOffset WhenUtc { get; set; }

        /// <summary>The action name, from the engine's fixed audit-action catalog.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>The subject (<c>sub</c>) the action concerns, when user-related.</summary>
        public string? Subject { get; set; }

        /// <summary>The OAuth client involved, when any.</summary>
        public string? ClientId { get; set; }

        /// <summary>The SSO session involved, when any.</summary>
        public string? Sid { get; set; }

        /// <summary>The W3C trace id correlating the action to distributed traces.</summary>
        public string? TraceId { get; set; }

        /// <summary>The caller's IP address, when the action was request-bound.</summary>
        public string? IpAddress { get; set; }

        /// <summary>The outcome (<c>success</c> / <c>failure</c>), when the action can fail.</summary>
        public string? Outcome { get; set; }

        /// <summary>Extra action-specific detail as a JSON object.</summary>
        public string? DetailsJson { get; set; }
    }
}
