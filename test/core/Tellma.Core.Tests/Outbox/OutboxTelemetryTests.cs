// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Calendars;
using Tellma.Core.Outbox;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The outbox's instruments: the emails enqueued counted by their request's source, which is
    ///     one of a closed set; the emails settled counted by state; and the wait of each one sent.
    /// </summary>
    public sealed class OutboxTelemetryTests : IDisposable
    {
        /// <summary>The meter factory the instruments are created from.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        [Fact]
        public void The_instruments_are_named_on_the_core_meter()
        {
            Assert.Equal(
                ("Tellma.Core", "tellma.outbox.enqueued", "tellma.outbox.settled", "tellma.outbox.latency", "source", "state"),
                (OutboxTelemetryNames.MeterName, OutboxTelemetryNames.Enqueued, OutboxTelemetryNames.Settled, OutboxTelemetryNames.Latency, OutboxTelemetryNames.SourceTag, OutboxTelemetryNames.StateTag));
        }

        [Fact]
        public void The_enqueued_emails_are_counted_one_per_row_by_source_with_no_point_for_a_source_absent()
        {
            using MetricCollector<long> enqueued = Collector<long>(OutboxTelemetryNames.Enqueued);
            OutboxMetrics metrics = new(Meters);

            metrics.RecordEnqueued([Request(EmailEnqueueRequest.ApiSource), Request(EmailEnqueueRequest.NotificationSource), Request(EmailEnqueueRequest.ApiSource)]);
            metrics.RecordEnqueued([Request(EmailEnqueueRequest.NotificationSource)]);

            Assert.Equal(
                [(2L, "api"), (1L, "notification"), (1L, "notification")],
                enqueued.GetMeasurementSnapshot().Select(static point => (point.Value, (string)point.Tags[OutboxTelemetryNames.SourceTag]!)));
        }

        [Fact]
        public void A_settled_email_is_counted_by_state_and_only_a_sent_one_records_its_wait()
        {
            using MetricCollector<long> settled = Collector<long>(OutboxTelemetryNames.Settled);
            using MetricCollector<double> latency = Collector<double>(OutboxTelemetryNames.Latency);
            OutboxMetrics metrics = new(Meters);

            metrics.RecordSettled(OutgoingEmailState.Sent, TimeSpan.FromSeconds(3.5));
            metrics.RecordSettled(OutgoingEmailState.Rejected, null);
            metrics.RecordSettled(OutgoingEmailState.Sandboxed, TimeSpan.FromMilliseconds(-5));

            Assert.Equal(
                ["sent", "rejected", "sandboxed"],
                settled.GetMeasurementSnapshot().Select(static point => (string)point.Tags[OutboxTelemetryNames.StateTag]!));

            // A database clock a little behind the row's stamp never records a negative wait.
            Assert.Equal([3.5, 0], latency.GetMeasurementSnapshot().Select(static point => point.Value));
        }

        [Fact]
        public void Every_state_has_a_lower_case_tag_value()
        {
            Assert.Equal(
                ["pending", "sending", "sent", "sandboxed", "rejected", "failed", "cancelled"],
                Enum.GetValues<OutgoingEmailState>().Select(OutboxMetrics.StateValue));
        }

        [Fact]
        public async Task A_request_of_another_source_is_refused_before_anything_is_enlisted()
        {
            using MetricCollector<long> enqueued = Collector<long>(OutboxTelemetryNames.Enqueued);
            var context = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch);
            EmailOutbox outbox = new(_services, new FixedContextAccessor(context), Options.Create(new OutboxOptions()), new OutboxMetrics(Meters));

            ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
                () => outbox.EnqueueAsync(Refusing.Of<IWriteHost>(), [Request(EmailEnqueueRequest.ApiSource), Request("webhook")], TestContext.Current.CancellationToken));

            Assert.Equal("requests", refused.ParamName);
            Assert.Contains("at 1", refused.Message, StringComparison.Ordinal);
            Assert.Empty(enqueued.GetMeasurementSnapshot());
        }

        /// <summary>The host's meter factory.</summary>
        private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

        /// <summary>A collector of one of the outbox's instruments.</summary>
        /// <typeparam name="T">The instrument's value type.</typeparam>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(Meters, OutboxTelemetryNames.MeterName, instrument);
        }

        /// <summary>A well-formed request of a source.</summary>
        /// <param name="source">The source.</param>
        /// <returns>The request.</returns>
        private static EmailEnqueueRequest Request(string source)
        {
            return new EmailEnqueueRequest(
                new EmailMessage { To = [new EmailAddress("ada@example.com")], Subject = "Hello", TextBody = "Hello there.", Audience = EmailAudience.Internal },
                Source: source);
        }
    }
}
