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

        /// <summary>The tag key of a problem's status.</summary>
        public const string StatusTag = "status";

        /// <summary>The tag key of a problem's code.</summary>
        public const string CodeTag = "code";

        private readonly Counter<long> _requestsRejected;
        private readonly Counter<long> _problems;
        private readonly Histogram<double> _operationDuration;

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
        }

        /// <summary>Counts a request refused before its endpoint ran.</summary>
        /// <param name="reason">Why it was refused.</param>
        public void RecordRejection(string reason)
        {
            _requestsRejected.Add(1, new KeyValuePair<string, object?>(ApiTelemetryNames.ReasonTag, reason));
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
