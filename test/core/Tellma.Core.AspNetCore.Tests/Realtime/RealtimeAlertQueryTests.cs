// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.Notifications;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The realtime hub's checked-in alert queries, checked back against its instruments: every
    ///     metric, dimension and compared value resolves to something the hub can emit, and every
    ///     instrument but the connection gauge and the event counter backs an alert.
    /// </summary>
    public sealed class RealtimeAlertQueryTests
    {
        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "RealtimeAlertQueries"));

        /// <summary>The hub's instruments.</summary>
        private static HashSet<string> Instruments =>
        [
            .. AlertQueries.Constants([typeof(RealtimeTelemetryNames)], static name => name != nameof(RealtimeTelemetryNames.MeterName) && !name.EndsWith("Tag", StringComparison.Ordinal)),
            ApiTelemetryNames.RealtimeEvents,
        ];

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_and_values_the_hub_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            HashSet<string> tags = [.. AlertQueries.Constants([typeof(RealtimeTelemetryNames)], static name => name.EndsWith("Tag", StringComparison.Ordinal)), ApiTelemetryNames.EventTag];

            // The outcomes, the close reasons and the event names the hub sends.
            HashSet<string> values =
            [
                .. AlertQueries.Constants([typeof(RealtimeMetrics), typeof(SessionEndedEvent)], static _ => true),
                SessionEndedEvent.Name,
            ];

            Assert.Empty(AlertQueries.Unresolved(queries, Instruments, tags, values));
        }

        [Fact]
        public void Every_instrument_but_the_connection_gauge_and_the_event_counter_backs_an_alert()
        {
            // The untold-revocation alert reads each close's own told tag, not the event counter.
            Assert.Equal([RealtimeTelemetryNames.Connections, ApiTelemetryNames.RealtimeEvents], AlertQueries.Uncovered(Queries, Instruments));
        }
    }
}
