// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The names of the client telemetry relay's instruments, their tags and its logger category,
    ///     on the web host's <see cref="MeterName" /> meter. Every tag takes a closed set of values, and
    ///     a value the client sends outside its set is counted as <c>other</c>, so no client string ever
    ///     becomes a tag; the tenant and the user are never tags.
    /// </summary>
    public static class ClientTelemetryNames
    {
        /// <summary>The web host's meter.</summary>
        public const string MeterName = "Tellma.Core.AspNetCore";

        /// <summary>
        ///     The category of every record the relay writes for the client, which a deployment may
        ///     route to a sink of its own or filter by level.
        /// </summary>
        public const string LoggerCategory = "Tellma.ClientTelemetry";

        /// <summary>
        ///     Counter (<c>{error}</c>): the error items the relay accepted. Tagged
        ///     <see cref="SourceTag" /> (<c>api</c>, <c>render</c> or <c>global</c>) and
        ///     <see cref="KindTag" /> (an API error's kind, or <c>none</c>).
        /// </summary>
        public const string Errors = "tellma.client.errors";

        /// <summary>
        ///     Counter (<c>{request}</c>): the client's slow requests the relay accepted, each a call that
        ///     reached 3 s. Tagged <see cref="OutcomeTag" /> (<c>ok</c>, <c>error</c>, <c>cancelled</c>
        ///     or <c>pending</c>).
        /// </summary>
        public const string SlowRequests = "tellma.client.requests.slow";

        /// <summary>The tag naming how an error reached the client's error handler.</summary>
        public const string SourceTag = "source";

        /// <summary>The tag naming an API error's kind.</summary>
        public const string KindTag = "kind";

        /// <summary>The tag naming how a slow request ended.</summary>
        public const string OutcomeTag = "outcome";
    }
}
