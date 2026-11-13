// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
using Tellma.Core.Webhooks;
using Tellma.Testing.Support.Observability;

namespace Tellma.Core.Email.Tests.Observability
{
    /// <summary>
    ///     The alerts the instruments were designed to back are checked in as queries, and this
    ///     checks the queries back against the instruments. Renaming a metric or a tag therefore
    ///     turns a stale alert into a failing build rather than into an alert that quietly stops
    ///     firing.
    /// </summary>
    public partial class AlertQueryTests
    {
        /// <summary>The types whose constants name the pipeline's instruments and tags.</summary>
        private static readonly Type[] TelemetryNames = [typeof(EmailTelemetryNames), typeof(WebhookTelemetryNames)];

        /// <summary>The checked-in queries, copied beside the suite.</summary>
        private static IReadOnlyList<AlertQuery> Queries => AlertQueries.Load(Path.Combine(AppContext.BaseDirectory, "AlertQueries"));

        /// <summary>The instruments the pipeline declares.</summary>
        private static HashSet<string> Instruments => AlertQueries.Constants(TelemetryNames, static name => name.Contains("Instrument", StringComparison.Ordinal));

        [Fact]
        public void Every_alert_query_references_only_instruments_tags_and_values_the_pipeline_emits()
        {
            IReadOnlyList<AlertQuery> queries = Queries;
            Assert.NotEmpty(queries);

            // Names alone are not enough: a query that filters on a value nothing emits matches
            // nothing, forever, without any error anywhere. Renaming "transient_failure" would
            // otherwise leave the failure-rate alert reporting a permanent zero.
            Assert.Empty(AlertQueries.Unresolved(
                queries,
                Instruments,
                AlertQueries.Constants(TelemetryNames, static name => name.EndsWith("Tag", StringComparison.Ordinal)),
                EmittableTagValues()));
        }

        [Fact]
        public void Every_instrument_the_pipeline_emits_is_covered_by_at_least_one_query()
        {
            // Two instruments exist to size and time a batch rather than to alert on it, and one to
            // time the webhook receiver; everything else must be answering some operational question.
            Assert.Equal(
                [EmailTelemetryNames.SendBatchSizeInstrument, EmailTelemetryNames.SendDurationInstrument,
                 WebhookTelemetryNames.RequestDurationInstrument],
                AlertQueries.Uncovered(Queries, Instruments));
        }

        [Fact]
        public void The_silent_webhook_alert_watches_exactly_the_transports_that_emit_delivery_events()
        {
            // This alert asks "live mail is going out, yet no delivery event came back", which is
            // only a question worth asking of a transport that has a callback at all — so it carries
            // its own list of them. That list is the one part of these queries the checks above
            // cannot reach: the names resolve, so nothing complains, but a transport merely missing
            // from the list is never evaluated by the alert built to catch precisely its silence.
            AlertQuery query = Assert.Single(Queries, static query => query.FileName == "silent-webhook.kql");

            Match binding = EventingTransportList().Match(query.Body);
            Assert.True(
                binding.Success,
                "silent-webhook.kql no longer declares its eventing transports as `let eventing = dynamic([…])`, so nothing checks them against the code any more.");

            // Both directions: a new eventing transport missing from the query fails here, and so
            // does a name the query still watches after it stopped emitting events.
            Assert.Equal(
                EmailDeliveryEventTransports.Names.Order(StringComparer.Ordinal),
                AlertQueries.Literals(binding.Groups["names"].Value).Order(StringComparer.Ordinal));
        }

        /// <summary>Every tag value the pipeline can emit.</summary>
        /// <returns>The values.</returns>
        private static HashSet<string> EmittableTagValues()
        {
            HashSet<string> values = AlertQueries.Constants(TelemetryNames, static _ => true);

            // The tag-value spellings live in a switch rather than in constants, precisely so a
            // rename in code cannot silently rewrite a dimension. Driving the switch is what ties
            // them to the queries.
            foreach (EmailSendOutcome outcome in Enum.GetValues<EmailSendOutcome>())
            {
                _ = values.Add(EmailDiagnostics.ToTagValue(outcome));
            }

            foreach (EmailAudience audience in Enum.GetValues<EmailAudience>())
            {
                _ = values.Add(EmailDiagnostics.ToTagValue(audience));
            }

            foreach (EmailDeliveryEventType type in Enum.GetValues<EmailDeliveryEventType>())
            {
                _ = values.Add(EmailDiagnostics.ToTagValue(type));
            }

            foreach (EmailChannel channel in Enum.GetValues<EmailChannel>())
            {
                _ = values.Add(EmailDiagnostics.ToTagValue(channel));
            }

            // Transport names belong to the connector packages, which this suite deliberately does
            // not reference — a core test that pulled in three connectors would invert the
            // dependency direction. The ones with a delivery-event callback are declared in the
            // contract assembly, so they come from there rather than from a second list here; the
            // eventless two are named below, which is the only other place a query may name a
            // transport at all.
            values.UnionWith(EmailDeliveryEventTransports.Names);
            _ = values.Add("smtp");
            _ = values.Add(EmailTransportResolution.LogSinkTransportName);

            return values;
        }

        // Anchored on the binding name rather than on dynamic() in general, so an unrelated dynamic
        // literal added to the query later cannot be mistaken for the eventing list.
        [GeneratedRegex(@"let\s+eventing\s*=\s*dynamic\(\[(?<names>[^\]]*)\]\)")]
        private static partial Regex EventingTransportList();
    }
}
