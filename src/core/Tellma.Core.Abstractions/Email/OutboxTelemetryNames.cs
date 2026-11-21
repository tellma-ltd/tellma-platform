// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The names of the email outbox's instruments and their tags, on the platform's
    ///     <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so they never
    ///     change. Every tag takes a closed set of values; the tenant, the addresses, the subjects and
    ///     the bodies are never tags.
    /// </summary>
    /// <remarks>
    ///     The email pipeline meters the sends and the delivery events themselves under its own meter,
    ///     with the outbox's emails tagged by their correlation's owner key; these instruments count
    ///     what the outbox stores and decides.
    /// </remarks>
    public static class OutboxTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>
        ///     Counter (<c>{email}</c>): outgoing emails enqueued, one per row, counted once the
        ///     frame the rows were enlisted with committed them; a frame that rolls back counts
        ///     nothing. Tagged <see cref="SourceTag" />.
        /// </summary>
        public const string Enqueued = "tellma.outbox.enqueued";

        /// <summary>
        ///     Counter (<c>{email}</c>): outgoing emails the sending job settled, counted once the
        ///     settle committed. Tagged <see cref="StateTag" />.
        /// </summary>
        public const string Settled = "tellma.outbox.settled";

        /// <summary>
        ///     Histogram (<c>s</c>): how long an email waited from its enqueue to its send, measured on
        ///     the database clock for every email that settled sent or sandboxed. Untagged.
        /// </summary>
        public const string Latency = "tellma.outbox.latency";

        /// <summary>The source tag: who produced the email, <c>api</c> or <c>notification</c>.</summary>
        public const string SourceTag = "source";

        /// <summary>The state tag: the state an email settled in, <c>sent</c>, <c>sandboxed</c>, <c>rejected</c> or <c>failed</c>.</summary>
        public const string StateTag = "state";
    }
}
