// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The SPA's checked-in alert queries, checked back against the client telemetry relay's
    ///     counters: every metric, dimension and compared value resolves to something the relay can
    ///     emit, and every relay counter backs an alert.
    /// </summary>
    public sealed class ClientAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "ClientAlertQueries"));

        /// <summary>The relay's instruments.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(
            [typeof(ClientTelemetryNames)],
            static name => name is not (nameof(ClientTelemetryNames.MeterName) or nameof(ClientTelemetryNames.LoggerCategory)) && !name.EndsWith("Tag", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_and_values_the_relay_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            HashSet<string> tags = AlertQueries.Constants([typeof(ClientTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal));
            HashSet<string> values =
            [
                .. ClientTelemetryMetrics.Sources,
                .. ClientTelemetryMetrics.ErrorKinds,
                .. ClientTelemetryMetrics.Outcomes,
                ClientTelemetryMetrics.Other,
                ClientTelemetryMetrics.NoKind,
            ];

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Every_relay_counter_backs_an_alert()
        {
            Assert.Equal(2, Instruments.Count);
            Assert.Empty(AlertQueries.Uncovered(Queries, Instruments));
        }
    }
}
