// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>The web API's instruments, on the host's meter.</summary>
    /// <remarks>Every tag value is a closed set: no instrument carries a tenant or a user.</remarks>
    internal sealed class ApiMetrics
    {
        /// <summary>The rejection reason of a request whose contract fingerprint is not the endpoint's.</summary>
        public const string ClientOutdatedReason = "client_outdated";

        /// <summary>The rejection reason of a request a rate or concurrency policy refused.</summary>
        public const string RateLimitedReason = "rate_limited";

        /// <summary>The tag key of a problem's status.</summary>
        public const string StatusTag = "status";

        /// <summary>The tag key of a problem's code.</summary>
        public const string CodeTag = "code";

        private readonly Counter<long> _requestsRejected;
        private readonly Counter<long> _problems;
        private readonly Histogram<double> _operationDuration;
        private readonly Counter<long> _unknownMembers;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public ApiMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(ApiTelemetryNames.MeterName);
            _requestsRejected = meter.CreateCounter<long>(
                ApiTelemetryNames.RequestsRejected,
                description: "Web API requests refused before their endpoint ran, by reason.");
            _problems = meter.CreateCounter<long>(
                ApiTelemetryNames.Problems,
                description: "Problem responses, by status and problem code.");
            _operationDuration = meter.CreateHistogram<double>(
                ApiTelemetryNames.OperationDuration,
                unit: "s",
                description: "How long a web API operation took from admission to its answer, by resource, operation and client.");
            _unknownMembers = meter.CreateCounter<long>(
                ApiTelemetryNames.UnknownMembers,
                description: "Request body members no property of their type declares, skipped, by type.");
        }

        /// <summary>Counts a request refused before its endpoint ran.</summary>
        /// <param name="reason">Why it was refused.</param>
        /// <param name="lane">The report lane it was refused on; null off the report lanes.</param>
        public void RecordRejection(string reason, string? lane = null)
        {
            if (lane is null)
            {
                _requestsRejected.Add(1, new KeyValuePair<string, object?>(ApiTelemetryNames.ReasonTag, reason));
            }
            else
            {
                _requestsRejected.Add(
                    1,
                    new KeyValuePair<string, object?>(ApiTelemetryNames.ReasonTag, reason),
                    new KeyValuePair<string, object?>(ApiTelemetryNames.LaneTag, lane));
            }
        }

        /// <summary>Counts a request body member no property of its type declares, which was skipped.</summary>
        /// <param name="type">The type's display name: a closed set, the types the wire carries.</param>
        public void RecordUnknownMember(string type)
        {
            _unknownMembers.Add(1, new KeyValuePair<string, object?>(ApiTelemetryNames.TypeTag, type));
        }

        /// <summary>Counts a problem response.</summary>
        /// <param name="status">The response's status.</param>
        /// <param name="code">The problem's code.</param>
        public void RecordProblem(int status, string code)
        {
            _problems.Add(
                1,
                new KeyValuePair<string, object?>(StatusTag, status),
                new KeyValuePair<string, object?>(CodeTag, code));
        }

        /// <summary>Records one operation's duration.</summary>
        /// <param name="resource">The stack's or route's resource.</param>
        /// <param name="operation">The standard segment or action name.</param>
        /// <param name="client">The calling client's name.</param>
        /// <param name="elapsed">How long the operation took.</param>
        public void RecordOperation(string resource, string operation, string client, TimeSpan elapsed)
        {
            _operationDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(ApiTelemetryNames.ResourceTag, resource),
                new KeyValuePair<string, object?>(ApiTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(ApiTelemetryNames.ClientTag, client));
        }
    }
}
