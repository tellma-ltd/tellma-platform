// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Outbox;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The choice, from one webhook call, of the one event per outgoing email the delivery update
    ///     applies: opens, clicks and unclassified events dropped, the strongest event kept, and the
    ///     events that name no email set apart.
    /// </summary>
    public sealed class DeliveryEventSelectionTests
    {
        /// <summary>The first event's time; each later one is a minute on.</summary>
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Opens_clicks_and_unclassified_events_are_dropped_and_say_nothing_about_delivery()
        {
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                Event(1, 7, EmailDeliveryEventType.Opened, "e1"),
                Event(1, 7, EmailDeliveryEventType.Clicked, "e2"),
                Event(1, 7, EmailDeliveryEventType.Other, "e3"),
            ]);

            Assert.Equal(3, selection.Dropped);
            Assert.Empty(selection.Tenants);
            Assert.Empty(selection.Unaddressed);
            Assert.Equal(
                [null, null, null],
                new[] { EmailDeliveryEventType.Opened, EmailDeliveryEventType.Clicked, EmailDeliveryEventType.Other }.Select(DeliveryEventSelection.StatusOf));
        }

        [Fact]
        public void Each_delivery_event_type_reports_the_status_of_the_same_name()
        {
            EmailDeliveryEventType[] types =
            [
                EmailDeliveryEventType.Deferred, EmailDeliveryEventType.Delivered, EmailDeliveryEventType.Dropped,
                EmailDeliveryEventType.Failed, EmailDeliveryEventType.Bounced, EmailDeliveryEventType.SpamReported,
            ];

            Assert.Equal(
                types.Select(static type => type.ToString()),
                types.Select(static type => DeliveryEventSelection.StatusOf(type)!.Value.ToString()));
        }

        [Fact]
        public void Each_row_keeps_its_strongest_event_whatever_order_the_provider_reported_them_in()
        {
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                Event(1, 7, EmailDeliveryEventType.Bounced, "e1", minute: 3),
                Event(1, 7, EmailDeliveryEventType.Delivered, "e2", minute: 1),
                Event(1, 7, EmailDeliveryEventType.Deferred, "e3", minute: 0),
                Event(1, 9, EmailDeliveryEventType.Deferred, "e4", minute: 0),
                Event(1, 9, EmailDeliveryEventType.Delivered, "e5", minute: 2),
            ]);

            TenantDeliveryEvents tenant = Assert.Single(selection.Tenants);
            Assert.Equal(1, tenant.TenantId);
            Assert.Equal(5, tenant.Weighed);
            Assert.Equal(
                [(0, 7, EmailDeliveryStatus.Bounced, (byte)EmailDeliveryStatus.Bounced, "e1"), (1, 9, EmailDeliveryStatus.Delivered, (byte)EmailDeliveryStatus.Delivered, "e5")],
                tenant.Rows.Select(static row => (row.Ordinal, row.OutgoingEmailId, row.Status, row.Rank, row.ProviderEventId)));
        }

        [Fact]
        public void Between_events_of_one_status_the_later_one_wins_and_between_equal_times_the_later_report()
        {
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                Event(1, 7, EmailDeliveryEventType.Deferred, "late", minute: 5),
                Event(1, 7, EmailDeliveryEventType.Deferred, "early", minute: 1),
                Event(1, 7, EmailDeliveryEventType.Deferred, "untimed", minute: null),
                Event(1, 8, EmailDeliveryEventType.Deferred, "first", minute: 2),
                Event(1, 8, EmailDeliveryEventType.Deferred, "second", minute: 2),
            ]);

            Assert.Equal(["late", "second"], Assert.Single(selection.Tenants).Rows.Select(static row => row.ProviderEventId));
        }

        [Fact]
        public void Events_are_grouped_by_tenant_in_tenant_order()
        {
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                Event(3, 7, EmailDeliveryEventType.Delivered, "e1"),
                Event(1, 7, EmailDeliveryEventType.Delivered, "e2"),
                Event(3, 2, EmailDeliveryEventType.Delivered, "e3"),
            ]);

            Assert.Equal(
                [(1, "7"), (3, "2,7")],
                selection.Tenants.Select(static tenant => (tenant.TenantId, string.Join(",", tenant.Rows.Select(static row => row.OutgoingEmailId)))));
        }

        [Fact]
        public void An_event_that_names_no_tenant_or_no_email_id_is_set_apart_as_unaddressed()
        {
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                new EmailDeliveryEvent(new EmailCorrelation("outbox", "7"), "ada@example.com", EmailDeliveryEventType.Delivered, "delivered", null, Start, "e1"),
                new EmailDeliveryEvent(new EmailCorrelation("outbox", "seven", 1), "ada@example.com", EmailDeliveryEventType.Delivered, "delivered", null, Start, "e2"),
                new EmailDeliveryEvent(new EmailCorrelation("outbox", "-7", 1), "ada@example.com", EmailDeliveryEventType.Delivered, "delivered", null, Start, "e3"),
                new EmailDeliveryEvent(new EmailCorrelation("outbox", "0", 1), "ada@example.com", EmailDeliveryEventType.Delivered, "delivered", null, Start, "e4"),
                new EmailDeliveryEvent(null, "ada@example.com", EmailDeliveryEventType.Delivered, "delivered", null, Start, "e5"),
            ]);

            Assert.Empty(selection.Tenants);
            Assert.Equal(
                [(null, "7"), (1, "seven"), (1, "-7"), (1, "0"), (null, string.Empty)],
                selection.Unaddressed.Select(static unaddressed => (unaddressed.TenantId, unaddressed.Reference)));
        }

        [Fact]
        public void A_provider_text_longer_than_its_column_is_cut_to_the_column()
        {
            EmailDeliveryEvent wordy = new(
                new EmailCorrelation("outbox", "7", 1),
                new string('r', 300),
                EmailDeliveryEventType.Bounced,
                "bounce",
                new string('x', 600),
                Start,
                new string('i', 150));

            EmailDeliveryEventList row = Assert.Single(Assert.Single(DeliveryEventSelection.Select([wordy]).Tenants).Rows);

            Assert.Equal(
                (OutgoingEmail.DeliveryRecipientLength, OutgoingEmail.DeliveryReasonLength, OutgoingEmail.ProviderEventIdLength),
                (row.Recipient!.Length, row.Reason!.Length, row.ProviderEventId.Length));
        }

        /// <summary>A delivery event of the outbox's.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="rowId">The outgoing email's id.</param>
        /// <param name="type">The event's classification.</param>
        /// <param name="providerEventId">The provider's event id.</param>
        /// <param name="minute">The minutes after <see cref="Start" /> it happened; null for no time.</param>
        /// <returns>The event.</returns>
        private static EmailDeliveryEvent Event(int tenantId, int rowId, EmailDeliveryEventType type, string providerEventId, int? minute = 0)
        {
            return new EmailDeliveryEvent(
                new EmailCorrelation(OutgoingEmailHandler.CorrelationOwnerKey, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture), tenantId),
                "ada@example.com",
                type,
                type.ToString().ToLowerInvariant(),
                Reason: null,
                minute is int at ? Start.AddMinutes(at) : null,
                providerEventId);
        }
    }
}
