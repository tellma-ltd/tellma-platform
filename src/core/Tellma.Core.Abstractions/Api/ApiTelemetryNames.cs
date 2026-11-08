// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The names of the web host's meter, its instruments and their tag keys. Tag values are
    ///     closed sets, and no instrument carries a tenant or user tag; logs carry those, metrics
    ///     never do.
    /// </summary>
    public static class ApiTelemetryNames
    {
        /// <summary>The web host's meter.</summary>
        public const string MeterName = "Tellma.Core.AspNetCore";

        /// <summary>
        ///     Counter of requests refused before they ran. Tags: <see cref="ReasonTag" />
        ///     (<c>rate_limited</c> or <c>client_outdated</c>), and <see cref="LaneTag" /> on the report
        ///     lanes.
        /// </summary>
        public const string RequestsRejected = "tellma.api.requests.rejected";

        /// <summary>Counter of problem responses. Tags: <c>status</c>, <c>code</c>.</summary>
        public const string Problems = "tellma.api.problems";

        /// <summary>
        ///     Histogram of operation durations, in seconds. Tags: <see cref="ResourceTag" />,
        ///     <see cref="OperationTag" />, <see cref="ClientTag" />.
        /// </summary>
        public const string OperationDuration = "tellma.api.operation.duration";

        /// <summary>Counter of request members skipped as unknown. Tag: <c>type</c>, the CLR type name.</summary>
        public const string UnknownMembers = "tellma.api.unknown_members";

        /// <summary>Counter of real-time events pushed to clients. Tag: <see cref="EventTag" />.</summary>
        public const string RealtimeEvents = "tellma.realtime.events";

        /// <summary>Tag key: the stack or route the operation belongs to; also an activity tag.</summary>
        public const string ResourceTag = "tellma.resource";

        /// <summary>Tag key: the standard segment or action name; also an activity tag.</summary>
        public const string OperationTag = "tellma.operation";

        /// <summary>Tag key: the calling client's name; also an activity tag.</summary>
        public const string ClientTag = "tellma.client";

        /// <summary>Tag key: why a request was refused.</summary>
        public const string ReasonTag = "reason";

        /// <summary>Tag key: the report lane a request was refused on.</summary>
        public const string LaneTag = "lane";

        /// <summary>Tag key: the real-time event's name.</summary>
        public const string EventTag = "event";
    }
}
