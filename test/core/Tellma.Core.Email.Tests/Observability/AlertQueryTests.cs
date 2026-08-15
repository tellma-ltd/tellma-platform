// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
using Tellma.Core.Webhooks;

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
        [Fact]
        public void Every_alert_query_references_only_instruments_and_tags_the_pipeline_emits()
        {
            string[] queries = Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "AlertQueries"), "*.kql");

            Assert.NotEmpty(queries);

            HashSet<string> declaredInstruments = DeclaredConstants(
                static name => name.Contains("Instrument", StringComparison.Ordinal));
            HashSet<string> declaredTags = DeclaredConstants(
                static name => name.EndsWith("Tag", StringComparison.Ordinal));

            List<string> unknown = [];
            foreach (string queryFile in queries)
            {
                string query = File.ReadAllText(queryFile);
                string fileName = Path.GetFileName(queryFile);

                foreach (Match match in MetricNameReference().Matches(query))
                {
                    string metric = match.Groups["name"].Value;
                    if (!declaredInstruments.Contains(metric))
                    {
                        unknown.Add($"{fileName}: metric '{metric}'");
                    }
                }

                foreach (Match match in DimensionReference().Matches(query))
                {
                    string tag = match.Groups["name"].Value;
                    if (!declaredTags.Contains(tag))
                    {
                        unknown.Add($"{fileName}: dimension '{tag}'");
                    }
                }
            }

            Assert.Empty(unknown);
        }

        [Fact]
        public void Every_tag_value_an_alert_query_compares_against_is_one_the_pipeline_can_emit()
        {
            // Names alone are not enough: a query that filters on a value nothing emits matches
            // nothing, forever, without any error anywhere. Renaming "transient_failure" would
            // otherwise leave the failure-rate alert reporting a permanent zero.
            HashSet<string> emittable = EmittableTagValues();

            List<string> unknown = [];
            foreach (string queryFile in Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "AlertQueries"), "*.kql"))
            {
                string query = QueryBody(File.ReadAllText(queryFile));
                string fileName = Path.GetFileName(queryFile);

                // Instrument names and dimension keys are checked by their own case above, so they
                // are excluded here rather than being counted as unknown values.
                HashSet<string> alreadyChecked = new(StringComparer.Ordinal);
                foreach (Match match in MetricNameReference().Matches(query))
                {
                    alreadyChecked.Add(match.Groups["name"].Value);
                }

                foreach (Match match in DimensionReference().Matches(query))
                {
                    alreadyChecked.Add(match.Groups["name"].Value);
                }

                foreach (Match match in QuotedLiteral().Matches(query))
                {
                    string literal = match.Groups["value"].Value;
                    if (!alreadyChecked.Contains(literal) && !emittable.Contains(literal))
                    {
                        unknown.Add($"{fileName}: value '{literal}'");
                    }
                }
            }

            Assert.Empty(unknown);
        }

        [Fact]
        public void Every_instrument_the_pipeline_emits_is_covered_by_at_least_one_query()
        {
            string queries = string.Concat(Directory
                .GetFiles(Path.Combine(AppContext.BaseDirectory, "AlertQueries"), "*.kql")
                .Select(File.ReadAllText)
                .Select(QueryBody));

            List<string> uncovered = [.. DeclaredConstants(
                    static name => name.Contains("Instrument", StringComparison.Ordinal))
                .Where(instrument => !queries.Contains(instrument, StringComparison.Ordinal))];

            // Two instruments exist to size and time a batch rather than to alert on it; everything
            // else must be answering some operational question.
            Assert.Equal(
                [EmailTelemetryNames.SendBatchSizeInstrument, EmailTelemetryNames.SendDurationInstrument,
                 WebhookTelemetryNames.RequestDurationInstrument],
                uncovered.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void The_silent_webhook_alert_watches_exactly_the_transports_that_emit_delivery_events()
        {
            // This alert asks "live mail is going out, yet no delivery event came back", which is
            // only a question worth asking of a transport that has a callback at all — so it carries
            // its own list of them. That list is the one part of these queries the checks above
            // cannot reach: the names resolve, so nothing complains, but a transport merely missing
            // from the list is never evaluated by the alert built to catch precisely its silence.
            string query = QueryBody(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "AlertQueries", "silent-webhook.kql")));

            Match binding = EventingTransportList().Match(query);
            Assert.True(
                binding.Success,
                "silent-webhook.kql no longer declares its eventing transports as `let eventing = dynamic([…])`, so nothing checks them against the code any more.");

            IEnumerable<string> queried = QuotedLiteral()
                .Matches(binding.Groups["names"].Value)
                .Select(static match => match.Groups["value"].Value);

            // Both directions: a new eventing transport missing from the query fails here, and so
            // does a name the query still watches after it stopped emitting events.
            Assert.Equal(
                EmailDeliveryEventTransports.Names.Order(StringComparer.Ordinal),
                queried.Order(StringComparer.Ordinal));
        }

        private static string QueryBody(string text)
        {
            // Comment lines are dropped: an instrument merely mentioned in the prose above a query
            // is not queried, and the prose also quotes alert names that are not tag values at all.
            return string.Join(
                '\n',
                text.Split('\n').Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }

        private static HashSet<string> EmittableTagValues()
        {
            HashSet<string> values = DeclaredConstants(static _ => true);

            // The tag-value spellings live in a switch rather than in constants, precisely so a
            // rename in code cannot silently rewrite a dimension. Driving the switch is what ties
            // them to the queries.
            foreach (EmailSendOutcome outcome in Enum.GetValues<EmailSendOutcome>())
            {
                values.Add(EmailDiagnostics.ToTagValue(outcome));
            }

            foreach (EmailAudience audience in Enum.GetValues<EmailAudience>())
            {
                values.Add(EmailDiagnostics.ToTagValue(audience));
            }

            foreach (EmailDeliveryEventType type in Enum.GetValues<EmailDeliveryEventType>())
            {
                values.Add(EmailDiagnostics.ToTagValue(type));
            }

            foreach (EmailChannel channel in Enum.GetValues<EmailChannel>())
            {
                values.Add(EmailDiagnostics.ToTagValue(channel));
            }

            // Transport names belong to the connector packages, which this suite deliberately does
            // not reference — a core test that pulled in three connectors would invert the
            // dependency direction. The ones with a delivery-event callback are declared in the
            // contract assembly, so they come from there rather than from a second list here; the
            // eventless two are named below, which is the only other place a query may name a
            // transport at all.
            foreach (string transport in EmailDeliveryEventTransports.Names)
            {
                values.Add(transport);
            }

            values.Add("smtp");
            values.Add(EmailTransportResolution.LogSinkTransportName);

            return values;
        }

        private static HashSet<string> DeclaredConstants(Func<string, bool> nameFilter)
        {
            HashSet<string> values = new(StringComparer.Ordinal);
            foreach (Type type in new[] { typeof(EmailTelemetryNames), typeof(WebhookTelemetryNames) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.IsLiteral && nameFilter(field.Name) && field.GetRawConstantValue() is string value)
                    {
                        values.Add(value);
                    }
                }
            }

            return values;
        }

        [GeneratedRegex("name\\s*==\\s*\"(?<name>[^\"]+)\"")]
        private static partial Regex MetricNameReference();

        [GeneratedRegex("""customDimensions\["(?<name>[^"]+)"\]""")]
        private static partial Regex DimensionReference();

        [GeneratedRegex("\"(?<value>[^\"]*)\"")]
        private static partial Regex QuotedLiteral();

        // Anchored on the binding name rather than on dynamic() in general, so an unrelated dynamic
        // literal added to the query later cannot be mistaken for the eventing list.
        [GeneratedRegex(@"let\s+eventing\s*=\s*dynamic\(\[(?<names>[^\]]*)\]\)")]
        private static partial Regex EventingTransportList();
    }
}
