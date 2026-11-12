// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     The names of the real-time hub's instruments and their tags, on the web host's
    ///     <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so they never
    ///     change. Every tag takes a closed set of values; the tenant and the user are never tags.
    /// </summary>
    /// <remarks>
    ///     The events pushed to clients count on <see cref="ApiTelemetryNames.RealtimeEvents" />, an
    ///     instrument of the same meter.
    /// </remarks>
    public static class RealtimeTelemetryNames
    {
        /// <summary>The web host's meter.</summary>
        public const string MeterName = "Tellma.Core.AspNetCore";

        /// <summary>Up-down counter (<c>{connection}</c>): the hub connections this instance holds open. Tagged <see cref="ModeTag" />.</summary>
        public const string Connections = "tellma.realtime.connections";

        /// <summary>
        ///     Counter (<c>{attempt}</c>): connection attempts and refused client invocations. Tagged
        ///     <see cref="OutcomeTag" />.
        /// </summary>
        public const string Connects = "tellma.realtime.connects";

        /// <summary>Counter (<c>{connection}</c>): connections the server closed. Tagged <see cref="ReasonTag" />.</summary>
        public const string Closes = "tellma.realtime.closes";

        /// <summary>The connect outcome tag: <c>connected</c>, <c>refused</c> or <c>invocation-refused</c>.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>The close reason tag: <c>session-terminated</c>, <c>access-revoked</c> or <c>tenant-unavailable</c>.</summary>
        public const string ReasonTag = "reason";

        /// <summary>The hosting mode tag: <c>in-process</c>, <c>redis</c> or <c>azure</c>.</summary>
        public const string ModeTag = "mode";
    }
}
