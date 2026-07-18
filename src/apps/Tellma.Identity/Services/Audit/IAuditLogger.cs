// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Audit
{
    /// <summary>One security-relevant action to record.</summary>
    public sealed class AuditEventEntry
    {
        /// <summary>The action name, from <see cref="AuditActions" />.</summary>
        public required string Action { get; init; }

        /// <summary>The subject (<c>sub</c>) the action concerns, when user-related.</summary>
        public string? Subject { get; init; }

        /// <summary>The OAuth client involved, when any.</summary>
        public string? ClientId { get; init; }

        /// <summary>The SSO session involved, when any.</summary>
        public string? Sid { get; init; }

        /// <summary>The caller's IP address, when request-bound.</summary>
        public string? IpAddress { get; init; }

        /// <summary>The outcome (<c>success</c>/<c>failure</c>), when the action can fail.</summary>
        public string? Outcome { get; init; }

        /// <summary>Extra action-specific detail as a JSON object.</summary>
        public string? DetailsJson { get; init; }
    }

    /// <summary>
    ///     Emits immutable audit events for every security-relevant action, carrying the subject,
    ///     client, and the current trace id so audit rows correlate with distributed traces.
    /// </summary>
    public interface IAuditLogger
    {
        /// <summary>Records one audit event. Failures are logged, never thrown into the flow.</summary>
        /// <param name="entry">The event to record.</param>
        /// <param name="cancellationToken">Aborts the write.</param>
        /// <returns>A task that completes when the event is persisted (or the failure logged).</returns>
        Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default);
    }
}
