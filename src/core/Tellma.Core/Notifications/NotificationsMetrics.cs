// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The notifications' instruments on the core meter: the notifier's rows created, recipients
    ///     suppressed, rows replaced and withdrawn, and delivery jobs enqueued, each by type; the
    ///     inbox's summaries, rows marked read and rows cleared, by verb; the preference saves; and
    ///     the rows retention deleted, by band. No instrument carries a tenant or a user.
    /// </summary>
    internal sealed class NotificationsMetrics
    {
        /// <summary>The verb tag of the rows marked read by name.</summary>
        public const string ReadVerb = "read";

        /// <summary>The verb tag of the rows marked read all at once.</summary>
        public const string ReadAllVerb = "read-all";

        /// <summary>The verb tag of the rows cleared by name.</summary>
        public const string ClearVerb = "clear";

        /// <summary>The verb tag of the read rows cleared all at once.</summary>
        public const string ClearReadVerb = "clear-read";

        /// <summary>The band tag of the read rows retention deletes.</summary>
        public const string ReadBand = "read";

        /// <summary>The band tag of the unread rows retention deletes.</summary>
        public const string UnreadBand = "unread";

        /// <summary>Rows inserted.</summary>
        private readonly Counter<long> _created;

        /// <summary>Recipients who received nothing: inactive, muted, or holding a duplicate.</summary>
        private readonly Counter<long> _suppressed;

        /// <summary>Unread duplicates refreshed in place.</summary>
        private readonly Counter<long> _replaced;

        /// <summary>Rows a withdrawal deleted.</summary>
        private readonly Counter<long> _withdrawn;

        /// <summary>Delivery jobs inserted.</summary>
        private readonly Counter<long> _deliveryEnqueued;

        /// <summary>Inbox summaries served.</summary>
        private readonly Counter<long> _summaries;

        /// <summary>Rows the inbox verbs marked read.</summary>
        private readonly Counter<long> _markedRead;

        /// <summary>Rows the inbox verbs deleted.</summary>
        private readonly Counter<long> _cleared;

        /// <summary>Preference saves committed.</summary>
        private readonly Counter<long> _preferencesSaved;

        /// <summary>Rows retention deleted.</summary>
        private readonly Counter<long> _retained;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public NotificationsMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(NotificationsTelemetryNames.MeterName);
            _created = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Created,
                "{notification}",
                "Inbox rows inserted by notification requests, by type.");
            _suppressed = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Suppressed,
                "{notification}",
                "Recipients who received no row: inactive, muted, or holding a duplicate, by type.");
            _replaced = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Replaced,
                "{notification}",
                "Unread duplicates refreshed in place, by type.");
            _withdrawn = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Withdrawn,
                "{notification}",
                "Rows deleted by withdrawals, by type.");
            _deliveryEnqueued = meter.CreateCounter<long>(
                NotificationsTelemetryNames.DeliveryEnqueued,
                "{job}",
                "Delivery jobs inserted, one per qualifying slice of a request, by type.");
            _summaries = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Summaries,
                "{request}",
                "Inbox summaries served.");
            _markedRead = meter.CreateCounter<long>(
                NotificationsTelemetryNames.MarkedRead,
                "{notification}",
                "Rows the inbox verbs marked read, by verb.");
            _cleared = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Cleared,
                "{notification}",
                "Rows the users deleted from their inboxes, by verb.");
            _preferencesSaved = meter.CreateCounter<long>(
                NotificationsTelemetryNames.PreferencesSaved,
                "{request}",
                "Saves of a user's notification preferences.");
            _retained = meter.CreateCounter<long>(
                NotificationsTelemetryNames.Retained,
                "{notification}",
                "Rows retention deleted, by band.");
        }

        /// <summary>Records what one committed request did.</summary>
        /// <param name="type">The request's type key.</param>
        /// <param name="inbox">Whether the type writes inbox rows; a channel-only request counts only its jobs.</param>
        /// <param name="outcome">What its statement did.</param>
        public void RecordNotify(string type, bool inbox, NotifyOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(outcome);

            KeyValuePair<string, object?> tag = new(NotificationsTelemetryNames.TypeTag, type);
            if (inbox)
            {
                // A recipient receives a refreshed row or a new one, never both, so the rest received nothing.
                int received = outcome.InsertedUserIds.Count + outcome.RefreshedUserIds.Count;
                Add(_created, outcome.InsertedUserIds.Count, tag);
                Add(_replaced, outcome.RefreshedUserIds.Count, tag);
                Add(_suppressed, Math.Max(0, outcome.Requested - received), tag);
            }

            Add(_deliveryEnqueued, outcome.JobIds.Count, tag);
        }

        /// <summary>Records what one committed withdrawal deleted.</summary>
        /// <param name="outcome">The deleted rows.</param>
        public void RecordWithdraw(WithdrawOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            foreach (IGrouping<string, WithdrawnRow> rows in outcome.Deleted.GroupBy(static row => row.Type, StringComparer.Ordinal))
            {
                _withdrawn.Add(rows.Count(), new KeyValuePair<string, object?>(NotificationsTelemetryNames.TypeTag, rows.Key));
            }
        }

        /// <summary>Records one inbox summary served.</summary>
        public void RecordSummary()
        {
            _summaries.Add(1);
        }

        /// <summary>Records the rows one committed verb marked read.</summary>
        /// <param name="verb">The verb: <see cref="ReadVerb" /> or <see cref="ReadAllVerb" />.</param>
        /// <param name="count">The rows newly marked read.</param>
        public void RecordMarkedRead(string verb, int count)
        {
            Add(_markedRead, count, new KeyValuePair<string, object?>(NotificationsTelemetryNames.VerbTag, verb));
        }

        /// <summary>Records the rows one committed verb deleted.</summary>
        /// <param name="verb">The verb: <see cref="ClearVerb" /> or <see cref="ClearReadVerb" />.</param>
        /// <param name="count">The rows deleted.</param>
        public void RecordCleared(string verb, int count)
        {
            Add(_cleared, count, new KeyValuePair<string, object?>(NotificationsTelemetryNames.VerbTag, verb));
        }

        /// <summary>Records one committed save of a user's preferences.</summary>
        public void RecordPreferencesSaved()
        {
            _preferencesSaved.Add(1);
        }

        /// <summary>Records the rows one retention page deleted.</summary>
        /// <param name="band">The band: <see cref="ReadBand" /> or <see cref="UnreadBand" />.</param>
        /// <param name="count">The rows deleted.</param>
        public void RecordRetained(string band, int count)
        {
            Add(_retained, count, new KeyValuePair<string, object?>(NotificationsTelemetryNames.BandTag, band));
        }

        /// <summary>Adds a positive amount to a counter; nothing for none, so an idle tag leaves no zero points.</summary>
        /// <param name="counter">The counter.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="tag">The counter's one tag.</param>
        private static void Add(Counter<long> counter, int amount, KeyValuePair<string, object?> tag)
        {
            if (amount > 0)
            {
                counter.Add(amount, tag);
            }
        }
    }
}
