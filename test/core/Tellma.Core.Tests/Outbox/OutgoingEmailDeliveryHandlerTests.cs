// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Outbox;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The delivery events' handler, over a fake registry and a fake writer: which tenants'
    ///     events it applies by their state, that a frozen tenant fails the call only after every
    ///     other tenant's were applied, and what it counts and logs.
    /// </summary>
    public sealed class OutgoingEmailDeliveryHandlerTests : IDisposable
    {
        /// <summary>The meter factory the instruments are created from.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <summary>Collects the handler's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The tenants the registry holds, by id.</summary>
        private readonly Dictionary<int, TenantState> _tenants = [];

        /// <summary>The writer, which records each call and answers from a script.</summary>
        private readonly RecordingWriter _writer = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _logs.Dispose();
            _services.Dispose();
        }

        [Fact]
        public void The_handler_answers_the_outbox_s_owner_key()
        {
            Assert.Equal("outbox", Handler(new OutboxMetrics(Meters)).OwnerKey);
        }

        [Fact]
        public async Task An_active_read_only_or_suspended_tenant_s_events_are_applied_and_a_retired_or_missing_tenant_s_dropped()
        {
            _tenants[1] = TenantState.Active;
            _tenants[2] = TenantState.ReadOnly;
            _tenants[3] = TenantState.Suspended;
            _tenants[4] = TenantState.Retired;
            _tenants[5] = TenantState.Provisioning;

            await Handler(new OutboxMetrics(Meters)).HandleAsync(
                [Event(1, 10), Event(2, 20), Event(3, 30), Event(4, 40), Event(5, 50), Event(6, 60)],
                TestContext.Current.CancellationToken);

            Assert.Equal([(1, "10"), (2, "20"), (3, "30")], _writer.Calls);
            Assert.Equal(
                [(4, "Retired"), (5, "Provisioning"), (6, "Missing")],
                Logged("OutboxDeliveryTenantSkipped").Select(static record => (Int(record, "TenantId"), Text(record, "State"))));
        }

        [Fact]
        public async Task A_frozen_tenant_fails_the_call_after_every_other_tenant_s_events_were_applied()
        {
            _tenants[1] = TenantState.Frozen;
            _tenants[2] = TenantState.Active;

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => Handler(new OutboxMetrics(Meters)).HandleAsync([Event(1, 10), Event(2, 20)], TestContext.Current.CancellationToken));

            Assert.Equal((1, TenantState.Frozen, TenantProblemCodes.Frozen), (refused.TenantId, refused.State, refused.Code));
            Assert.Equal([(2, "20")], _writer.Calls);
            Assert.Equal([1], Logged("OutboxDeliveryTenantFrozen").Select(static record => Int(record, "TenantId")));
        }

        [Fact]
        public async Task A_tenant_whose_update_fails_does_not_keep_the_next_tenant_s_events_from_being_applied()
        {
            _tenants[1] = TenantState.Active;
            _tenants[2] = TenantState.Active;
            InvalidOperationException broken = new("The tenant's database is unreachable.");
            _writer.Failures[1] = broken;

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Handler(new OutboxMetrics(Meters)).HandleAsync([Event(1, 10), Event(2, 20)], TestContext.Current.CancellationToken));

            Assert.Same(broken, thrown);
            Assert.Equal([(1, "10"), (2, "20")], _writer.Calls);
        }

        [Fact]
        public async Task Several_failures_and_a_freeze_are_raised_together()
        {
            _tenants[1] = TenantState.Active;
            _tenants[2] = TenantState.Frozen;
            _tenants[3] = TenantState.Active;
            _writer.Failures[1] = new InvalidOperationException("First.");
            _writer.Failures[3] = new InvalidOperationException("Third.");

            AggregateException thrown = await Assert.ThrowsAsync<AggregateException>(
                () => Handler(new OutboxMetrics(Meters)).HandleAsync([Event(1, 10), Event(2, 20), Event(3, 30)], TestContext.Current.CancellationToken));

            Assert.Equal(
                [typeof(InvalidOperationException), typeof(InvalidOperationException), typeof(TenantUnavailableException)],
                thrown.InnerExceptions.Select(static inner => inner.GetType()));
        }

        [Fact]
        public async Task Each_event_is_counted_applied_stale_unmatched_or_tenant_skipped()
        {
            using MetricCollector<long> updates = new(Meters, OutboxTelemetryNames.MeterName, OutboxTelemetryNames.DeliveryUpdates);
            _tenants[1] = TenantState.Active;
            _tenants[4] = TenantState.Retired;

            // Tenant 1: three events weighed for rows 10 and 11 (two of row 10, one of row 11) and two
            // of row 12; the writer applies one, finds row 12 missing, so both of its events are
            // unmatched, and leaves the rest as stale.
            _writer.Answers[1] = new DeliveryUpdateResult(1, [12]);

            await Handler(new OutboxMetrics(Meters)).HandleAsync(
                [Event(1, 10), Event(1, 10, EmailDeliveryEventType.Delivered), Event(1, 11), Event(1, 12), Event(1, 12, EmailDeliveryEventType.Bounced), Event(4, 40), Unaddressed(), Opened(1, 10)],
                TestContext.Current.CancellationToken);

            Assert.Equal(
                [("applied", 1L), ("stale", 2L), ("tenant_skipped", 1L), ("unmatched", 3L)],
                updates.GetMeasurementSnapshot()
                    .GroupBy(static point => (string)point.Tags[OutboxTelemetryNames.ResultTag]!)
                    .Select(static group => (group.Key, group.Sum(static point => point.Value)))
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal));
            Assert.Equal(
                [(null, "seven"), (1, "12")],
                Logged("OutboxDeliveryEventUnmatched").Select(static record => (NullableInt(record, "TenantId"), Text(record, "Reference"))));
        }

        [Fact]
        public void A_result_outside_the_closed_set_is_refused()
        {
            OutboxMetrics metrics = new(Meters);

            ArgumentException refused = Assert.Throws<ArgumentException>(() => metrics.RecordDeliveryUpdates("bounced", 1));

            Assert.Equal("result", refused.ParamName);
            Assert.Equal(["applied", "stale", "unmatched", "tenant_skipped"], OutboxMetrics.Results);
        }

        [Fact]
        public void The_retained_emails_are_counted_untagged_with_no_point_for_none()
        {
            using MetricCollector<long> retained = new(Meters, OutboxTelemetryNames.MeterName, OutboxTelemetryNames.Retained);
            OutboxMetrics metrics = new(Meters);

            metrics.RecordRetained(0);
            metrics.RecordRetained(500);
            metrics.RecordRetained(3);

            Assert.Equal([(500L, 0), (3L, 0)], retained.GetMeasurementSnapshot().Select(static point => (point.Value, point.Tags.Count)));
            Assert.Equal(
                ("tellma.outbox.delivery_updates", "tellma.outbox.retained", "result"),
                (OutboxTelemetryNames.DeliveryUpdates, OutboxTelemetryNames.Retained, OutboxTelemetryNames.ResultTag));
        }

        /// <summary>The host's meter factory.</summary>
        private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

        /// <summary>The handler over the fakes.</summary>
        /// <param name="metrics">The instruments it records to.</param>
        /// <returns>The handler.</returns>
        private OutgoingEmailDeliveryHandler Handler(OutboxMetrics metrics)
        {
            return new OutgoingEmailDeliveryHandler(new FakeRegistry(_tenants), _writer, metrics, new LoggerFactory([_logs]));
        }

        /// <summary>The records of one log event.</summary>
        /// <param name="eventName">The event's name.</param>
        /// <returns>The records, in order.</returns>
        private IEnumerable<FakeLogRecord> Logged(string eventName)
        {
            return _logs.Collector.GetSnapshot().Where(record => record.Id.Name == eventName);
        }

        /// <summary>A structured value of a record, as text.</summary>
        /// <param name="record">The record.</param>
        /// <param name="key">The value's name.</param>
        /// <returns>The value.</returns>
        private static string? Text(FakeLogRecord record, string key)
        {
            return record.StructuredState!.Single(pair => pair.Key == key).Value;
        }

        /// <summary>A structured integer of a record.</summary>
        /// <param name="record">The record.</param>
        /// <param name="key">The value's name.</param>
        /// <returns>The value.</returns>
        private static int Int(FakeLogRecord record, string key)
        {
            return int.Parse(Text(record, key)!, CultureInfo.InvariantCulture);
        }

        /// <summary>A structured integer of a record that may be null.</summary>
        /// <param name="record">The record.</param>
        /// <param name="key">The value's name.</param>
        /// <returns>The value; null for none.</returns>
        private static int? NullableInt(FakeLogRecord record, string key)
        {
            return Text(record, key) is string text ? int.Parse(text, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>A delivery event of an outgoing email of a tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="rowId">The email's id.</param>
        /// <param name="type">The event's classification.</param>
        /// <returns>The event.</returns>
        private static EmailDeliveryEvent Event(int tenantId, int rowId, EmailDeliveryEventType type = EmailDeliveryEventType.Deferred)
        {
            return new EmailDeliveryEvent(
                new EmailCorrelation("outbox", rowId.ToString(CultureInfo.InvariantCulture), tenantId),
                "ada@example.com",
                type,
                "raw",
                null,
                DateTimeOffset.UnixEpoch,
                Guid.NewGuid().ToString("N"));
        }

        /// <summary>An open, which the handler drops uncounted.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="rowId">The email's id.</param>
        /// <returns>The event.</returns>
        private static EmailDeliveryEvent Opened(int tenantId, int rowId)
        {
            return Event(tenantId, rowId, EmailDeliveryEventType.Opened);
        }

        /// <summary>An event whose correlation names no tenant and whose reference is no id.</summary>
        /// <returns>The event.</returns>
        private static EmailDeliveryEvent Unaddressed()
        {
            return new EmailDeliveryEvent(new EmailCorrelation("outbox", "seven"), null, EmailDeliveryEventType.Bounced, "bounce", null, null, "evt");
        }

        /// <summary>A registry that holds a fixed set of tenants in given states.</summary>
        /// <param name="states">The tenants' states, by id.</param>
        private sealed class FakeRegistry(IReadOnlyDictionary<int, TenantState> states) : ITenantRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<TenantInfo> Tenants => [.. states.Keys.Select(Get)];

            /// <inheritdoc />
            public Guid SnapshotVersion => Guid.Empty;

            /// <inheritdoc />
            public DateTimeOffset? SnapshotLoadedAt => DateTimeOffset.UnixEpoch;

            /// <inheritdoc />
            public bool IsAvailable => true;

            /// <inheritdoc />
            public TenantInfo? Find(int tenantId)
            {
                return states.TryGetValue(tenantId, out TenantState state)
                    ? new TenantInfo(
                        new TenantDescriptor(tenantId, "Acme", null, null, ["en"], TenantCategory.Live, state, null),
                        new TenantLocation("server", "database", "default"),
                        "Server=server;Database=database",
                        null,
                        Guid.Empty)
                    : null;
            }

            /// <inheritdoc />
            public TenantInfo Get(int tenantId)
            {
                return Find(tenantId) ?? throw new KeyNotFoundException($"Tenant {tenantId} is not held.");
            }

            /// <inheritdoc />
            public Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A writer that records each tenant's rows and answers, or fails, from a script.</summary>
        private sealed class RecordingWriter : IOutboxDeliveryWriter
        {
            /// <summary>Each call's tenant and its rows' ids, comma-separated, in order.</summary>
            public List<(int TenantId, string RowIds)> Calls { get; } = [];

            /// <summary>The answer per tenant; a tenant with none has every row applied.</summary>
            public Dictionary<int, DeliveryUpdateResult> Answers { get; } = [];

            /// <summary>The failure per tenant, thrown after the call is recorded.</summary>
            public Dictionary<int, Exception> Failures { get; } = [];

            /// <inheritdoc />
            public Task<DeliveryUpdateResult> ApplyAsync(int tenantId, IReadOnlyList<EmailDeliveryEventList> events, CancellationToken cancellationToken)
            {
                Calls.Add((tenantId, string.Join(",", events.Select(static row => row.OutgoingEmailId))));
                return Failures.TryGetValue(tenantId, out Exception? failure)
                    ? Task.FromException<DeliveryUpdateResult>(failure)
                    : Task.FromResult(Answers.TryGetValue(tenantId, out DeliveryUpdateResult? answer) ? answer : new DeliveryUpdateResult(events.Count, []));
            }
        }
    }
}
